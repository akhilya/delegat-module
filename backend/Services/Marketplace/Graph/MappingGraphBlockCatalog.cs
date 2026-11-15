using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Services.Marketplace.Graph;

/// <summary>
/// Canvas block catalog for the mapping editor and the cabinet assistant.
/// Sizes match frontend <c>graphLayout.js</c> estimates (used to place nodes, not CSS).
/// Handles match Vue Flow pin ids in the node components.
/// </summary>
public static class MappingGraphBlockCatalog
{
    public const int RankSep = 88;
    public const int NodeSep = 40;
    public const int AdapterWidth = 360;
    public const int AdapterHeader = 30;
    public const int AdapterRow = 24;
    public const int CardWidth = 180;
    public const int CardHeader = 26;
    public const int CardPinRow = 18;
    public const int SourceHeight = 54;
    public const int RerouteSize = 16;
    public const string AddFieldHandle = "f:__add__";

    private static readonly HashSet<string> SourceTypes =
    [
        "attribute", "storeAttribute", "productField", "static", "groupName",
        "storeFileUrl", "storeFileContent", "productFileByName",
        "productFileContentByName", "productFilesByExtension",
    ];

    private static readonly HashSet<string> ModifiersWithParamPins =
    [
        "multiply", "divide", "add", "subtract", "round", "clamp",
        "append", "prepend", "replace", "regexReplace", "padStart", "padEnd",
    ];

    private static readonly HashSet<string> MultiInputOps =
    [
        "formatString", "join", "makeArray", "arrayAppend", "arrayPrepend",
        "min", "max",
    ];

    private static readonly (string Type, object Inputs, string Output, string[]? Accepts)[] OpSpecs =
    [
        ("formatString", "multi", "string", null),
        ("join", "multi", "string", null),
        ("makeArray", "multi", "array", null),
        ("arrayAppend", 2, "array", null),
        ("splitToArray", "single", "array", ["string", "text", "select"]),
        ("arrayIndex", "single", "string", ["array", "multiselect"]),
        ("arrayLength", "single", "number", ["array", "multiselect"]),
        ("ifElse", 3, "any", null),
        ("not", "single", "boolean", null),
        ("and", "multi", "boolean", null),
        ("or", "multi", "boolean", null),
        ("equals", 2, "boolean", null),
        ("notEquals", 2, "boolean", null),
        ("greaterThan", 2, "boolean", ["number", "integer"]),
        ("lessThan", 2, "boolean", ["number", "integer"]),
        ("contains", 2, "boolean", null),
        ("isEmpty", "single", "boolean", null),
        ("isValid", "single", "boolean", null),
        ("add", "single", "number", ["number", "integer"]),
        ("subtract", "single", "number", ["number", "integer"]),
        ("multiply", "single", "number", ["number", "integer"]),
        ("divide", "single", "number", ["number", "integer"]),
        ("min", "multi", "number", ["number", "integer"]),
        ("max", "multi", "number", ["number", "integer"]),
    ];

    /// <summary>JSON payload for GET graph / GET mapping-graph/blocks.</summary>
    public static object Describe() => new
    {
        layout = new
        {
            rankSep = RankSep,
            nodeSep = NodeSep,
            sinkLead = 72,
            sinkGap = 48,
            note = "Left-to-right: sources → modifiers/ops → sinks. Set node.position. Use estimate width/height so cards do not overlap. Sources have no input. Output handle is always out.",
        },
        pinTypes = new[] { "string", "select", "number", "integer", "boolean", "multiselect", "array", "any" },
        synonyms = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = "string",
            ["select"] = "string",
            ["integer"] = "number",
            ["multiselect"] = "array",
        },
        handles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["out"] = "out",
            ["in"] = "in",
            ["field"] = "f:{adapterFieldKey}",
            ["addCustomSink"] = AddFieldHandle,
            ["paramOverride"] = "p:{paramKey}",
            ["opIndexed"] = "in-{i}",
            ["llmField"] = "field:{id}",
        },
        sources = SourceTypes.Select(t => new
        {
            type = t,
            kind = "source",
            width = CardWidth,
            height = t == "groupName" || t == "static" ? SourceHeight + 6 : SourceHeight,
            inputs = Array.Empty<object>(),
            outputs = new[] { new { id = "out", pinType = t == "groupName" ? "string" : "any", side = "right" } },
        }),
        sinks = new object[]
        {
            new
            {
                type = "adapterFields",
                kind = "sink",
                width = AdapterWidth,
                height = "30 + (fieldCount + 1) * 24 + 6",
                inputs = new object[]
                {
                    new { id = "f:{key}", pinType = "field.outputType", side = "left" },
                    new { id = AddFieldHandle, pinType = "any", side = "left", note = "drop here to create a mapping-local sink" },
                },
                outputs = Array.Empty<object>(),
            },
            new
            {
                type = "adapterPriceFields",
                kind = "sink",
                width = AdapterWidth,
                height = "30 + fieldCount * 24 + groupCount * 18 + 6",
                inputs = new[] { new { id = "f:{key}", pinType = "field.outputType", side = "left" } },
                outputs = Array.Empty<object>(),
            },
        },
        modifier = new
        {
            type = "modifier",
            kind = "modifier",
            width = CardWidth,
            height = "26 + pinRows * 18 + 14 (+18 if params, +36 if preview)",
            inputs = new object[]
            {
                new { id = "in", pinType = "value", side = "left" },
                new { id = "p:{paramKey}", pinType = "number|string", side = "left", note = "optional override for number/text params" },
                new { id = "field:{id}", pinType = "string", side = "left", note = "llmGenerate extra pins" },
            },
            outputs = new[] { new { id = "out", pinType = "depends on modifierType", side = "right" } },
            types = ModifierPipeline.KnownModifierTypes.OrderBy(t => t, StringComparer.Ordinal).ToArray(),
            paramPinModifiers = ModifiersWithParamPins.OrderBy(t => t, StringComparer.Ordinal).ToArray(),
        },
        ops = OpSpecs.Select(o => new
        {
            type = o.Type,
            kind = "op",
            width = CardWidth,
            height = MultiInputOps.Contains(o.Type) ? (object)"54 + max(2,inputCount)*10" : SourceHeight,
            inputs = OpInputs(o.Inputs),
            outputs = new[] { new { id = "out", pinType = o.Output, side = "right" } },
            accepts = o.Accepts,
        }),
        reroute = new
        {
            type = "reroute",
            kind = "reroute",
            width = RerouteSize,
            height = 18,
            inputs = new[] { new { id = "in", pinType = "any", side = "left" } },
            outputs = new[] { new { id = "out", pinType = "any", side = "right" } },
        },
    };

    public static (int Width, int Height) Estimate(GraphNode? node)
    {
        if (node == null) return (CardWidth, SourceHeight);

        if (node.type is "adapterFields" or "adapterPriceFields")
        {
            var n = node.data?.fields?.Count ?? 1;
            var extra = node.type == "adapterFields" ? 1 : 0;
            return (AdapterWidth, AdapterHeader + (n + extra) * AdapterRow + 6);
        }

        if (node.type == "modifier")
        {
            var modType = node.data?.modifierType;
            var pinRows = ModifiersWithParamPins.Contains(modType ?? "") ? 2 : 1;
            if (modType == "llmGenerate")
            {
                pinRows = 1 + (node.data?.fieldPins?.Count ?? 0);
                if (node.data?.@params?.ContainsKey("seed") == true) pinRows += 1;
            }
            var h = CardHeader + pinRows * CardPinRow + 14;
            if (node.data?.@params is { Count: > 0 }) h += 18;
            return (CardWidth, h);
        }

        if (MultiInputOps.Contains(node.type ?? ""))
        {
            var count = Math.Max(2, node.data?.inputCount ?? 2);
            return (CardWidth, 54 + count * 10);
        }

        if (node.type == "reroute") return (RerouteSize, 18);

        var sh = SourceHeight;
        if (!string.IsNullOrEmpty(node.data?.label) || !string.IsNullOrEmpty(node.data?.staticValue))
            sh += 6;
        return (CardWidth, sh);
    }

    /// <summary>Place new nodes left of the sink, aligned to the target pin, without overlapping.</summary>
    public static void PlaceWestOf(
        MappingGraph graph,
        IReadOnlyList<GraphNode> chainLeftToRight,
        GraphNode sink,
        string? sinkHandle)
    {
        if (chainLeftToRight.Count == 0) return;
        sink.position ??= new GraphPosition { x = 400, y = 0 };

        var x = sink.position.x;
        var pinY = PinCenterY(sink, sinkHandle);
        for (var i = chainLeftToRight.Count - 1; i >= 0; i--)
        {
            var node = chainLeftToRight[i];
            var (w, h) = Estimate(node);
            x -= RankSep + w;
            node.position = new GraphPosition { x = x, y = pinY - h / 2.0 };
            NudgeFree(graph, node);
            x = node.position.x;
        }
    }

    public static double PinCenterY(GraphNode node, string? handle)
    {
        var y = node.position?.y ?? 0;
        if (node.type is "adapterFields" or "adapterPriceFields"
            && handle != null
            && handle.StartsWith("f:", StringComparison.Ordinal)
            && handle != AddFieldHandle)
        {
            var key = handle[2..];
            var fields = node.data?.fields ?? [];
            var idx = fields.FindIndex(f => f.fieldKey == key);
            if (idx < 0) idx = Math.Max(0, fields.Count);
            return y + AdapterHeader + idx * AdapterRow + AdapterRow / 2.0;
        }

        var (_, h) = Estimate(node);
        return y + h / 2.0;
    }

    private static void NudgeFree(MappingGraph graph, GraphNode node)
    {
        var (w, h) = Estimate(node);
        for (var iter = 0; iter < 48; iter++)
        {
            var hit = false;
            foreach (var other in graph.nodes)
            {
                if (other.id == node.id || other.position == null) continue;
                var (ow, oh) = Estimate(other);
                if (BoxesOverlap(
                        node.position!.x, node.position.y, w, h,
                        other.position.x, other.position.y, ow, oh,
                        NodeSep))
                {
                    node.position.y += NodeSep + oh;
                    hit = true;
                    break;
                }
            }
            if (!hit) return;
        }
    }

    public static bool BoxesOverlap(
        double ax, double ay, double aw, double ah,
        double bx, double by, double bw, double bh,
        double gap = 0)
        => !(ax + aw + gap <= bx || bx + bw + gap <= ax || ay + ah + gap <= by || by + bh + gap <= ay);

    private static object OpInputs(object spec) => spec switch
    {
        "single" => new[] { new { id = "in", pinType = "any", side = "left" } },
        "multi" => new[]
        {
            new { id = "in-0", pinType = "any", side = "left" },
            new { id = "in-1", pinType = "any", side = "left" },
        },
        int n => Enumerable.Range(0, n).Select(i => new { id = $"in-{i}", pinType = "any", side = "left" }).ToArray(),
        _ => new[] { new { id = "in", pinType = "any", side = "left" } },
    };
}
