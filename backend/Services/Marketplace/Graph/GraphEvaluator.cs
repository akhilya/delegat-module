using System.Globalization;
using System.Text.Json;
using PrintO.Services.Marketplace.Modifiers;

namespace PrintO.Services.Marketplace.Graph;

/// <summary>
/// Evaluates a <see cref="MappingGraph"/> for one product, producing the same
/// field-key → value map shape as the legacy per-field pipeline.
///
/// Design: every modifier node reuses <see cref="ModifierPipeline.ApplySingle"/> — the exact
/// same implementations as the linear chain — so an auto-migrated single-line graph yields
/// identical output to the legacy path. The only genuinely new behaviour is fan-in
/// (formatString / join), which the linear chain cannot express.
/// </summary>
public static class GraphEvaluator
{
    /// <summary>Value flowing along a wire, plus metadata propagated down the branch.</summary>
    private readonly struct PinValue
    {
        public readonly object? Value;
        /// <summary>Options of the originating attribute (for the toText modifier).</summary>
        public readonly List<SourceOption>? OriginOptions;
        /// <summary>Type forced by an upstream forceType modifier; overrides the output node's type.</summary>
        public readonly string? ForcedType;

        public PinValue(object? value, List<SourceOption>? originOptions = null, string? forcedType = null)
        {
            Value = value;
            OriginOptions = originOptions;
            ForcedType = forcedType;
        }
    }

    public static GraphEvalResult Evaluate(MappingGraph graph, GraphEvaluationContext ctx)
    {
        var result = new GraphEvalResult();
        if (graph?.nodes == null || graph.nodes.Count == 0)
            return result;

        var nodesById = new Dictionary<string, GraphNode>();
        foreach (var n in graph.nodes)
            if (!string.IsNullOrEmpty(n.id))
                nodesById[n.id] = n;

        // Incoming edges per node, ordered by target-handle index (for multi-input combine nodes).
        var incoming = new Dictionary<string, List<GraphEdge>>();
        var outgoing = new Dictionary<string, List<GraphEdge>>();
        foreach (var e in graph.edges ?? new List<GraphEdge>())
        {
            if (string.IsNullOrEmpty(e.source) || string.IsNullOrEmpty(e.target))
                continue;
            if (!nodesById.ContainsKey(e.source) || !nodesById.ContainsKey(e.target))
                continue;

            (incoming.TryGetValue(e.target, out var inl) ? inl : incoming[e.target] = new()).Add(e);
            (outgoing.TryGetValue(e.source, out var outl) ? outl : outgoing[e.source] = new()).Add(e);
        }
        foreach (var list in incoming.Values)
            list.Sort((a, b) => HandleIndex(a.targetHandle).CompareTo(HandleIndex(b.targetHandle)));

        var order = TopologicalOrder(nodesById, incoming, outgoing);

        var pins = new Dictionary<string, PinValue>();

        foreach (var nodeId in order)
        {
            var node = nodesById[nodeId];
            var data = node.data ?? new GraphNodeData();

            try
            {
                switch (node.type)
                {
                    case "attribute":
                    {
                        var id = data.attributeId ?? 0;
                        var val = ApplySourceType(ctx.ResolveCategoryAttribute(id), data.valueType);
                        pins[nodeId] = new PinValue(val, ctx.ResolveCategoryAttributeOptions(id));
                        break;
                    }
                    case "storeAttribute":
                    {
                        var id = data.attributeId ?? 0;
                        var val = ApplySourceType(ctx.ResolveStoreAttribute(id), data.valueType);
                        pins[nodeId] = new PinValue(val, ctx.ResolveStoreAttributeOptions(id));
                        break;
                    }
                    case "productField":
                        pins[nodeId] = new PinValue(
                            ApplySourceType(ctx.ResolveProductField(data.field ?? ""), data.valueType));
                        break;
                    case "static":
                        // Backs the typed literal nodes too — coerce the stored string by valueType.
                        pins[nodeId] = new PinValue(
                            ctx.Coerce(ApplySourceType(data.staticValue, data.valueType), data.valueType));
                        break;
                    case "groupName":
                        pins[nodeId] = new PinValue(ctx.ResolveGroupName());
                        break;
                    case "storeFileUrl":
                        pins[nodeId] = new PinValue(ctx.ResolveStoreFileUrl(data.storeFileId ?? 0));
                        break;
                    case "storeFileContent":
                        pins[nodeId] = new PinValue(ctx.ResolveStoreFileContent(data.storeFileId ?? 0));
                        break;
                    case "productFileByName":
                        pins[nodeId] = new PinValue(ctx.ResolveProductFileUrlByName(data.fileName));
                        break;
                    case "productFileContentByName":
                        pins[nodeId] = new PinValue(ctx.ResolveProductFileContentByName(data.fileName));
                        break;
                    case "productFilesByExtension":
                        pins[nodeId] = new PinValue(ctx.ResolveProductFilesByExtension(data.fileExtension));
                        break;
                    case "reroute":
                        // Transparent pass-through (a wire bend) — forward the input pin unchanged.
                        pins[nodeId] = SingleInput(incoming, pins, nodeId);
                        break;

                    case "modifier":
                    {
                        var input = SingleInput(incoming, pins, nodeId);
                        var nodeCtx = ctx.ModifierContext == null
                            ? null
                            : new ModifierContext
                            {
                                ResolveReference = ctx.ModifierContext.ResolveReference,
                                SourceAttributeOptions = input.OriginOptions
                            };
                        var outValue = ModifierPipeline.ApplySingle(
                            data.modifierType ?? "", input.Value, data.@params, nodeCtx);

                        var forced = string.Equals(data.modifierType, "forceType", StringComparison.OrdinalIgnoreCase)
                            ? GetStringParam(data.@params, "type") ?? input.ForcedType
                            : input.ForcedType;

                        pins[nodeId] = new PinValue(outValue, input.OriginOptions, forced);
                        break;
                    }

                    case "formatString":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        pins[nodeId] = new PinValue(FormatTemplate(data.template, inputs));
                        break;
                    }
                    case "join":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        var delim = data.delimiter ?? "";
                        var parts = inputs
                            .Select(ToInvariantString)
                            .Where(s => !string.IsNullOrEmpty(s));
                        pins[nodeId] = new PinValue(string.Join(delim, parts));
                        break;
                    }

                    case "makeArray":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        pins[nodeId] = new PinValue(
                            inputs.Where(v => v != null).Select(ToInvariantString).Cast<object?>().ToList());
                        break;
                    }
                    case "arrayAppend":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        var list = ToList(inputs.Count > 0 ? inputs[0] : null);
                        if (inputs.Count > 1 && inputs[1] != null)
                            list.Add(ToInvariantString(inputs[1]));
                        pins[nodeId] = new PinValue(list);
                        break;
                    }
                    case "splitToArray":
                    {
                        var input = SingleInput(incoming, pins, nodeId);
                        var text = input.Value?.ToString() ?? "";
                        var delim = string.IsNullOrEmpty(data.delimiter) ? "," : data.delimiter!;
                        var parts = text
                            .Split(new[] { delim }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => x.Length > 0)
                            .Cast<object?>()
                            .ToList();
                        pins[nodeId] = new PinValue(parts);
                        break;
                    }
                    case "arrayIndex":
                    {
                        var input = SingleInput(incoming, pins, nodeId);
                        var list = ToList(input.Value);
                        var idx = data.index ?? 0;
                        pins[nodeId] = new PinValue(idx >= 0 && idx < list.Count ? list[idx] : null);
                        break;
                    }
                    case "arrayLength":
                    {
                        var input = SingleInput(incoming, pins, nodeId);
                        pins[nodeId] = new PinValue((long)ToList(input.Value).Count);
                        break;
                    }

                    // ---- General-purpose logic / predicate blocks ----
                    case "ifElse":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        var cond = inputs.Count > 0 && IsTruthy(inputs[0]);
                        var branch = cond ? 1 : 2;
                        pins[nodeId] = new PinValue(inputs.Count > branch ? inputs[branch] : null);
                        break;
                    }
                    case "not":
                        pins[nodeId] = new PinValue(!IsTruthy(SingleInput(incoming, pins, nodeId).Value));
                        break;
                    case "and":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        pins[nodeId] = new PinValue(inputs.Count > 0 && inputs.All(IsTruthy));
                        break;
                    }
                    case "or":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        pins[nodeId] = new PinValue(inputs.Any(IsTruthy));
                        break;
                    }
                    case "equals":
                    case "notEquals":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        var a = inputs.Count > 0 ? inputs[0] : null;
                        var b = inputs.Count > 1 ? inputs[1] : null;
                        var eq = ValuesEqual(a, b);
                        pins[nodeId] = new PinValue(node.type == "equals" ? eq : !eq);
                        break;
                    }
                    case "greaterThan":
                    case "lessThan":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        // Evaluate both unconditionally so `a`/`b` are definitely assigned (no && short-circuit).
                        var hasA = TryToDecimal(inputs.Count > 0 ? inputs[0] : null, out var a);
                        var hasB = TryToDecimal(inputs.Count > 1 ? inputs[1] : null, out var b);
                        var res = hasA && hasB && (node.type == "greaterThan" ? a > b : a < b);
                        pins[nodeId] = new PinValue(res);
                        break;
                    }
                    case "contains":
                    {
                        var inputs = AllInputs(incoming, pins, nodeId);
                        pins[nodeId] = new PinValue(ContainsValue(
                            inputs.Count > 0 ? inputs[0] : null,
                            inputs.Count > 1 ? inputs[1] : null));
                        break;
                    }
                    case "isEmpty":
                        pins[nodeId] = new PinValue(IsEmpty(SingleInput(incoming, pins, nodeId).Value));
                        break;
                    case "isValid":
                        pins[nodeId] = new PinValue(!IsEmpty(SingleInput(incoming, pins, nodeId).Value));
                        break;

                    case "adapterFields":
                    {
                        var fieldsByKey = (data.fields ?? new List<AdapterField>())
                            .Where(f => !string.IsNullOrEmpty(f.fieldKey))
                            .GroupBy(f => f.fieldKey!)
                            .ToDictionary(g => g.Key, g => g.First().outputType);

                        if (incoming.TryGetValue(nodeId, out var inEdges))
                        {
                            foreach (var e in inEdges)
                            {
                                var handle = e.targetHandle;
                                if (string.IsNullOrEmpty(handle) || !handle.StartsWith("f:"))
                                    continue;
                                var key = handle.Substring(2);
                                if (!pins.TryGetValue(e.source, out var pv))
                                    continue;

                                var declared = fieldsByKey.TryGetValue(key, out var t) ? t : null;
                                var effectiveType = pv.ForcedType ?? declared;
                                result.Outputs[key] = ctx.Coerce(pv.Value, effectiveType);
                                if (!string.IsNullOrEmpty(effectiveType))
                                    result.FieldTypes[key] = effectiveType!;
                            }
                        }
                        break;
                    }

                    case "output":
                    {
                        var input = SingleInput(incoming, pins, nodeId);
                        var effectiveType = input.ForcedType ?? data.outputType;
                        var outVal = ctx.Coerce(input.Value, effectiveType);
                        var key = data.fieldKey;
                        if (!string.IsNullOrEmpty(key))
                        {
                            result.Outputs[key] = outVal;
                            if (!string.IsNullOrEmpty(effectiveType))
                                result.FieldTypes[key] = effectiveType!;
                        }
                        break;
                    }
                }
            }
            catch
            {
                // A misconfigured node must not abort the whole export; leave its pin unset (null).
                if (!pins.ContainsKey(nodeId))
                    pins[nodeId] = new PinValue(null);
            }

            // Capture the value at this node for per-node preview (sink nodes write Outputs instead).
            if (pins.TryGetValue(nodeId, out var produced))
                result.NodeValues[nodeId] = produced.Value;
        }

        return result;
    }

    private static PinValue SingleInput(Dictionary<string, List<GraphEdge>> incoming,
        Dictionary<string, PinValue> pins, string nodeId)
    {
        if (incoming.TryGetValue(nodeId, out var edges) && edges.Count > 0
            && pins.TryGetValue(edges[0].source, out var pv))
            return pv;
        return new PinValue(null);
    }

    private static List<object?> AllInputs(Dictionary<string, List<GraphEdge>> incoming,
        Dictionary<string, PinValue> pins, string nodeId)
    {
        var values = new List<object?>();
        if (incoming.TryGetValue(nodeId, out var edges))
            foreach (var e in edges)
                values.Add(pins.TryGetValue(e.source, out var pv) ? pv.Value : null);
        return values;
    }

    // ---- Logic / predicate helpers ----

    /// <summary>Truthiness for the logic blocks: null / ""/"false"/"0"/"нет"/"no" / 0 / empty list ⇒ false.</summary>
    private static bool IsTruthy(object? v)
    {
        switch (v)
        {
            case null:
                return false;
            case bool b:
                return b;
            case string s:
                var t = s.Trim().ToLowerInvariant();
                return t is not ("" or "false" or "0" or "нет" or "no");
            case System.Collections.IEnumerable en:
                return en.Cast<object?>().Any();
        }
        if (TryToDecimal(v, out var d))
            return d != 0;
        return true;
    }

    /// <summary>Empty = null, whitespace string, or empty collection.</summary>
    private static bool IsEmpty(object? v)
    {
        return v switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            System.Collections.IEnumerable en => !en.Cast<object?>().Any(),
            _ => false
        };
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (TryToDecimal(a, out var da) && TryToDecimal(b, out var db))
            return da == db;
        return string.Equals(ToInvariantString(a), ToInvariantString(b), StringComparison.Ordinal);
    }

    private static bool TryToDecimal(object? v, out decimal result)
    {
        switch (v)
        {
            case decimal dec:
                result = dec;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case double db:
                result = (decimal)db;
                return true;
        }
        return decimal.TryParse(v?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>"contains": membership for lists, substring for everything else.</summary>
    private static bool ContainsValue(object? haystack, object? needle)
    {
        if (haystack is string hs)
            return hs.Contains(ToInvariantString(needle), StringComparison.OrdinalIgnoreCase);
        if (haystack is System.Collections.IEnumerable en)
        {
            var n = ToInvariantString(needle);
            return en.Cast<object?>().Any(x => string.Equals(ToInvariantString(x), n, StringComparison.Ordinal));
        }
        return false;
    }

    /// <summary>Coerce a pin value to a list (a plain string is a single element, not a char list).</summary>
    private static List<object?> ToList(object? value)
    {
        if (value is string s)
            return new List<object?> { s };
        if (value is System.Collections.IEnumerable en)
            return en.Cast<object?>().ToList();
        if (value == null)
            return new List<object?>();
        return new List<object?> { value };
    }

    private static List<string> TopologicalOrder(
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, List<GraphEdge>> incoming,
        Dictionary<string, List<GraphEdge>> outgoing)
    {
        var indegree = nodes.Keys.ToDictionary(id => id, _ => 0);
        foreach (var (target, edges) in incoming)
            indegree[target] = edges.Count;

        var queue = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<string>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(id);
            if (!outgoing.TryGetValue(id, out var edges)) continue;
            foreach (var e in edges)
            {
                if (--indegree[e.target] == 0)
                    queue.Enqueue(e.target);
            }
        }
        // Nodes left in a cycle are intentionally dropped (their outputs evaluate to null).
        return order;
    }

    private static object? ApplySourceType(object? value, string? valueType)
    {
        if (string.Equals(valueType, "multiselect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(valueType, "array", StringComparison.OrdinalIgnoreCase))
            return DeserializeMultiselect(value);
        return value;
    }

    /// <summary>Mirrors the multiselect deserialization in MappingService.BuildExportDataAsync.</summary>
    private static object? DeserializeMultiselect(object? value)
    {
        if (value is not string s || string.IsNullOrEmpty(s))
            return value;

        if (s.StartsWith('['))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(s);
                if (list != null && list.Count > 0)
                    return list;
            }
            catch (JsonException) { }
        }
        else if (s.Contains(','))
        {
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
                return parts.ToList();
        }
        return value;
    }

    private static string FormatTemplate(string? template, List<object?> inputs)
    {
        if (string.IsNullOrEmpty(template))
            return string.Concat(inputs.Select(ToInvariantString));

        var s = template;
        for (var i = 0; i < inputs.Count; i++)
            s = s.Replace("{" + i + "}", ToInvariantString(inputs[i]));
        return s;
    }

    private static string ToInvariantString(object? v)
    {
        switch (v)
        {
            case null:
                return "";
            case string s:
                return s;
            case System.Collections.IEnumerable en:
                return string.Join(", ", en.Cast<object?>().Select(ToInvariantString));
            case IFormattable f:
                return f.ToString(null, CultureInfo.InvariantCulture);
            default:
                return v.ToString() ?? "";
        }
    }

    private static string? GetStringParam(Dictionary<string, object>? p, string key)
    {
        if (p == null || !p.TryGetValue(key, out var v) || v == null)
            return null;
        if (v is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        return v.ToString();
    }

    /// <summary>Trailing-integer index of a handle name (e.g. "in-2" → 2), else 0.</summary>
    private static int HandleIndex(string? handle)
    {
        if (string.IsNullOrEmpty(handle)) return 0;
        var i = handle.Length;
        while (i > 0 && char.IsDigit(handle[i - 1])) i--;
        return i < handle.Length && int.TryParse(handle.AsSpan(i), out var n) ? n : 0;
    }
}
