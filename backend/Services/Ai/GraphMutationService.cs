using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Services.Ai;

public class GraphMutationService
{
    private readonly DataContext _context;

    private static readonly JsonSerializerOptions _opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GraphMutationService(DataContext context)
    {
        _context = context;
    }

    // Finds a field in the sink node; when absent but present in the adapter definitions,
    // appends it to the graph (same additive behavior as GraphGenerator.SyncAdapterFields).
    private static AdapterField ResolveField(GraphNode sinkNode, string adapterFieldKey, ExternalField? fieldDef)
    {
        var field = sinkNode.data!.fields!.FirstOrDefault(f => f.fieldKey == adapterFieldKey);
        if (field != null)
        {
            if (!string.IsNullOrEmpty(field.aliasOf))
                throw new InvalidOperationException(
                    $"Поле '{adapterFieldKey}' объединено с '{field.aliasOf}' — подключай '{field.aliasOf}' или сначала вызови unmerge_adapter_fields");
            return field;
        }

        if (fieldDef == null)
            throw new InvalidOperationException(
                $"Поле '{adapterFieldKey}' не найдено ни в графе, ни среди полей адаптера — проверь ключ через get_external_fields");

        field = new AdapterField
        {
            fieldKey = fieldDef.Key,
            label = fieldDef.Name,
            outputType = fieldDef.Type,
            isRequired = fieldDef.IsRequired,
        };
        sinkNode.data.fields.Add(field);
        return field;
    }

    // Connect a field directly (AdapterFieldSource — no intermediate nodes)
    public async Task ConnectFieldAsync(int mappingId, string adapterFieldKey,
        string sourceType, string? sourceField, int? sourceAttributeId,
        ExternalField? fieldDef = null, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");

        var field = ResolveField(sinkNode, adapterFieldKey, fieldDef);

        field.source = new AdapterFieldSource
        {
            type = sourceType,
            field = sourceField,
            attributeId = sourceAttributeId
        };

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    // Remove a direct connection from a field
    public async Task DisconnectFieldAsync(int mappingId, string adapterFieldKey, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var sinkNode = FindAdapterFieldsNode(graph);
        var field = sinkNode?.data?.fields?.FirstOrDefault(f => f.fieldKey == adapterFieldKey);
        if (field != null)
        {
            field.source = null;
            // Also remove any edges going into this field handle
            graph.edges.RemoveAll(e => e.targetHandle == $"f:{adapterFieldKey}");
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    // Set a static value for a field (creates static node + edge, removes direct source).
    // When fieldDef with options is provided, creates a "Стандартное значение" node instead:
    // value is resolved against the adapter's predefined options (by value or label).
    public async Task SetStaticValueAsync(int mappingId, string adapterFieldKey, string value,
        ExternalField? fieldDef = null, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");

        var field = ResolveField(sinkNode, adapterFieldKey, fieldDef);

        // Clear direct source
        field.source = null;
        // Remove existing edges to this field
        graph.edges.RemoveAll(e => e.targetHandle == $"f:{adapterFieldKey}");

        var nodeId = $"ai-static-{adapterFieldKey}-{Guid.NewGuid().ToString("N")[..8]}";
        var data = BuildStaticNodeData(value, fieldDef);
        graph.nodes.Add(new GraphNode { id = nodeId, type = "static", data = data });

        graph.edges.Add(new GraphEdge
        {
            id = $"e-{nodeId}-sink",
            source = nodeId,
            target = sinkNode.id,
            sourceHandle = "out",
            targetHandle = $"f:{adapterFieldKey}"
        });

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    // Add a modifier to a field chain
    public async Task AddModifierAsync(int mappingId, string adapterFieldKey,
        string modifierType, Dictionary<string, object>? @params,
        ExternalField? fieldDef = null, CancellationToken ct = default)
    {
        if (!ModifierPipeline.IsKnownModifier(modifierType))
            throw new InvalidOperationException(
                $"Модификатор '{modifierType}' не существует. Допустимые типы: {string.Join(", ", ModifierPipeline.KnownModifierTypes)}");

        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found");

        var field = ResolveField(sinkNode, adapterFieldKey, fieldDef);

        // Find current edge going to this field
        var existingEdge = graph.edges.FirstOrDefault(e => e.targetHandle == $"f:{adapterFieldKey}");
        var modNodeId = $"ai-mod-{adapterFieldKey}-{Guid.NewGuid().ToString("N")[..8]}";

        graph.nodes.Add(new GraphNode
        {
            id = modNodeId,
            type = "modifier",
            data = new GraphNodeData { modifierType = modifierType, @params = @params }
        });

        if (existingEdge != null)
        {
            // Insert modifier between current source and the sink
            var prevSourceId = existingEdge.source;
            graph.edges.Remove(existingEdge);

            graph.edges.Add(new GraphEdge
            {
                id = $"e-{prevSourceId}-{modNodeId}",
                source = prevSourceId,
                target = modNodeId,
                sourceHandle = "out",
                targetHandle = "in"
            });
        }

        graph.edges.Add(new GraphEdge
        {
            id = $"e-{modNodeId}-sink",
            source = modNodeId,
            target = sinkNode.id,
            sourceHandle = "out",
            targetHandle = $"f:{adapterFieldKey}"
        });

        // If field had direct source, convert to a source node + wire
        if (field.source != null)
        {
            var src = field.source;
            field.source = null;
            var srcNodeId = $"ai-src-{adapterFieldKey}-{Guid.NewGuid().ToString("N")[..8]}";

            graph.nodes.Add(await BuildSourceNodeAsync(srcNodeId, src.type ?? "static", src.field, src.attributeId, ct));

            graph.edges.Add(new GraphEdge
            {
                id = $"e-{srcNodeId}-{modNodeId}",
                source = srcNodeId,
                target = modNodeId,
                sourceHandle = "out",
                targetHandle = "in"
            });
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    // Analyze coverage of a mapping
    public async Task<MappingCoverage> GetCoverageAsync(int mappingId, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var sinkNode = FindAdapterFieldsNode(graph);
        var fields = sinkNode?.data?.fields ?? [];

        var total = fields.Count;
        var connected = fields.Count(f => IsFieldConnected(f, graph));
        var requiredTotal = fields.Count(f => f.isRequired);
        var requiredConnected = fields.Count(f => f.isRequired && IsFieldConnected(f, graph));
        var unmappedRequired = fields.Where(f => f.isRequired && !IsFieldConnected(f, graph)).ToList();

        return new MappingCoverage
        {
            Total = total,
            Connected = connected,
            Unconnected = total - connected,
            RequiredTotal = requiredTotal,
            RequiredConnected = requiredConnected,
            UnmappedRequiredFields = unmappedRequired.Select(f => new FieldInfo
            {
                FieldKey = f.fieldKey ?? "",
                Label = f.label ?? f.fieldKey ?? "",
                IsRequired = f.isRequired
            }).ToList()
        };
    }

    // Merge duplicate adapter fields: aliases export the primary's value under their own key
    // and are excluded from import. Own connections of alias fields are removed.
    public async Task MergeFieldsAsync(int mappingId, string primaryFieldKey, List<string> aliasFieldKeys, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");

        var byKey = sinkNode.data.fields
            .Where(f => !string.IsNullOrEmpty(f.fieldKey))
            .ToDictionary(f => f.fieldKey!);

        if (!byKey.TryGetValue(primaryFieldKey, out var primary))
            throw new InvalidOperationException($"Поле '{primaryFieldKey}' не найдено в маппинге");
        if (!string.IsNullOrEmpty(primary.aliasOf))
            throw new InvalidOperationException($"Поле '{primaryFieldKey}' само является алиасом '{primary.aliasOf}' — сначала разъедини его");

        foreach (var key in aliasFieldKeys)
        {
            if (key == primaryFieldKey)
                throw new InvalidOperationException("Поле не может быть алиасом самого себя");
            if (!byKey.TryGetValue(key, out var alias))
                throw new InvalidOperationException($"Поле '{key}' не найдено в маппинге");
            if (byKey.Values.Any(f => f.aliasOf == key))
                throw new InvalidOperationException($"Поле '{key}' является primary для других алиасов — сначала разъедини их");

            alias.aliasOf = primaryFieldKey;
            // Remove the alias's own connections — its value now comes from the primary
            alias.source = null;
            graph.edges.RemoveAll(e => e.targetHandle == $"f:{key}");
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UnmergeFieldsAsync(int mappingId, List<string> fieldKeys, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");

        foreach (var key in fieldKeys)
        {
            var field = sinkNode.data.fields.FirstOrDefault(f => f.fieldKey == key);
            if (field == null)
                throw new InvalidOperationException($"Поле '{key}' не найдено в маппинге");
            field.aliasOf = null;
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    // Per-field connection state for the AI: what each adapter field is, whether it's
    // connected and to what. Source strings: "attribute:933", "productField:name",
    // "static:\"XXL\"", "chain" (node chain with modifiers), null when unconnected.
    public async Task<List<FieldState>> GetFieldStatesAsync(int mappingId, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var sinkNode = FindAdapterFieldsNode(graph);
        var fields = sinkNode?.data?.fields ?? [];

        var connectedKeys = new HashSet<string>(fields
            .Where(f => f.fieldKey != null && IsFieldConnected(f, graph))
            .Select(f => f.fieldKey!));

        var states = new List<FieldState>();
        foreach (var f in fields)
        {
            // Alias fields inherit the primary's connection state
            if (!string.IsNullOrEmpty(f.aliasOf))
            {
                states.Add(new FieldState
                {
                    FieldKey = f.fieldKey ?? "",
                    Label = f.label ?? f.fieldKey ?? "",
                    Type = f.outputType ?? "string",
                    IsRequired = f.isRequired,
                    Connected = connectedKeys.Contains(f.aliasOf),
                    Source = $"alias of {f.aliasOf}",
                    AliasOf = f.aliasOf,
                });
                continue;
            }

            string? source = null;
            if (f.source != null)
            {
                source = f.source.type switch
                {
                    "productField" => $"productField:{f.source.field}",
                    "attribute" => $"attribute:{f.source.attributeId}",
                    "storeAttribute" => $"storeAttribute:{f.source.attributeId}",
                    _ => f.source.type,
                };
            }
            else
            {
                var edge = graph.edges.FirstOrDefault(e => e.targetHandle == $"f:{f.fieldKey}");
                if (edge != null)
                {
                    var srcNode = graph.nodes.FirstOrDefault(n => n.id == edge.source);
                    source = srcNode?.type switch
                    {
                        "static" => $"static:\"{srcNode.data?.staticValue}\"",
                        "productField" => $"productField:{srcNode.data?.field}",
                        "attribute" => $"attribute:{srcNode.data?.attributeId}",
                        "storeAttribute" => $"storeAttribute:{srcNode.data?.attributeId}",
                        _ => "chain", // modifier/op chain — inspect in the editor
                    };
                }
            }

            states.Add(new FieldState
            {
                FieldKey = f.fieldKey ?? "",
                Label = f.label ?? f.fieldKey ?? "",
                Type = f.outputType ?? "string",
                IsRequired = f.isRequired,
                Connected = source != null,
                Source = source,
            });
        }
        return states;
    }

    // Apply a list of AI suggestions to the graph (used by AiMappingService)
    public async Task<SuggestionApplyResult> ApplySuggestionsAsync(int mappingId,
        IReadOnlyList<AiFieldSuggestion> suggestions,
        IReadOnlyCollection<Delegat.Models.Marketplace.DTOs.ExternalField>? adapterFieldDefs = null,
        CancellationToken ct = default)
    {
        var result = new SuggestionApplyResult();
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null) return result;

        var applied = 0;
        foreach (var suggestion in suggestions)
        {
            var field = sinkNode.data.fields.FirstOrDefault(f => f.fieldKey == suggestion.AdapterFieldKey);
            if (field == null)
            {
                // Field missing from the graph — append it when the adapter actually defines it
                var def = adapterFieldDefs?.FirstOrDefault(d => d.Key == suggestion.AdapterFieldKey);
                if (def == null)
                {
                    if (suggestion.AdapterFieldKey != null) result.UnknownKeys.Add(suggestion.AdapterFieldKey);
                    continue;
                }
                field = ResolveField(sinkNode, suggestion.AdapterFieldKey!, def);
            }
            // Alias fields take the primary's value — connecting them separately would undo the merge
            if (!string.IsNullOrEmpty(field.aliasOf)) { result.AlreadyConnected++; continue; }
            if (IsFieldConnected(field, graph)) { result.AlreadyConnected++; continue; }

            if (suggestion.SourceType == null) { result.NoSource++; continue; }

            // Drop invented modifier types (e.g. REGEXEXTRACT) — keep the connection itself
            List<AiModifierSpec>? modifiers = null;
            if (suggestion.Modifiers is { Count: > 0 })
            {
                modifiers = suggestion.Modifiers.Where(m => ModifierPipeline.IsKnownModifier(m.Type)).ToList();
                foreach (var bad in suggestion.Modifiers.Where(m => !ModifierPipeline.IsKnownModifier(m.Type)))
                    result.InvalidModifiers.Add(bad.Type);
                if (modifiers.Count == 0) modifiers = null;
            }

            if (suggestion.SourceType == "static" && suggestion.StaticValue != null)
            {
                // Prefer a "Стандартное значение" node bound to adapter options over a free-text constant
                var fieldDef = adapterFieldDefs?.FirstOrDefault(d => d.Key == suggestion.AdapterFieldKey);
                GraphNodeData data;
                try { data = BuildStaticNodeData(suggestion.StaticValue, fieldDef); }
                catch (InvalidOperationException)
                {
                    // Suggested value doesn't match any allowed option — skip just this field
                    result.NoSource++;
                    continue;
                }
                var nodeId = $"ai-static-{field.fieldKey}-{Guid.NewGuid().ToString("N")[..8]}";
                graph.nodes.Add(new GraphNode { id = nodeId, type = "static", data = data });
                AddModifierChain(graph, nodeId, sinkNode.id, field.fieldKey!, modifiers);
                applied++;
            }
            else
            {
                field.source = new AdapterFieldSource
                {
                    type = suggestion.SourceType,
                    field = suggestion.SourceField,
                    attributeId = suggestion.SourceAttributeId
                };

                if (modifiers?.Count > 0)
                {
                    // Convert direct source to nodes for modifier chain
                    field.source = null;
                    var srcNodeId = $"ai-src-{field.fieldKey}-{Guid.NewGuid().ToString("N")[..8]}";
                    graph.nodes.Add(await BuildSourceNodeAsync(
                        srcNodeId, suggestion.SourceType, suggestion.SourceField, suggestion.SourceAttributeId, ct));
                    AddModifierChain(graph, srcNodeId, sinkNode.id, field.fieldKey!, modifiers);
                }

                applied++;
            }
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
        result.Applied = applied;
        return result;
    }

    // Builds node data for a static value. If the adapter field has predefined options,
    // produces a "Стандартное значение" node: staticValue = option value(s), label = option label(s).
    // Accepts either the option value or its label (case-insensitive); comma-separated for multiselect.
    internal static GraphNodeData BuildStaticNodeData(string value, Delegat.Models.Marketplace.DTOs.ExternalField? fieldDef)
    {
        if (fieldDef?.Options is not { Count: > 0 })
            return new GraphNodeData { staticValue = value, label = value };

        var isMulti = string.Equals(fieldDef.Type, "multiselect", StringComparison.OrdinalIgnoreCase);
        List<string> inputs = isMulti
            ? value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()
            : new List<string> { value.Trim() };

        var values = new List<string>();
        var labels = new List<string>();
        foreach (var input in inputs)
        {
            var opt = fieldDef.Options.FirstOrDefault(o =>
                    string.Equals(o.Value, input, StringComparison.OrdinalIgnoreCase))
                ?? fieldDef.Options.FirstOrDefault(o =>
                    string.Equals(o.Label, input, StringComparison.OrdinalIgnoreCase));
            if (opt == null)
            {
                var sample = string.Join(", ", fieldDef.Options.Take(15).Select(o => $"\"{o.Label}\""));
                throw new InvalidOperationException(
                    $"Значение \"{input}\" не входит в допустимые опции поля \"{fieldDef.Name}\". Доступно: {sample}" +
                    (fieldDef.Options.Count > 15 ? $" и ещё {fieldDef.Options.Count - 15}" : ""));
            }
            values.Add(opt.Value ?? input);
            labels.Add(opt.Label ?? opt.Value ?? input);
        }

        return new GraphNodeData
        {
            staticValue = string.Join(",", values),
            label = string.Join(", ", labels),
            isAdapterValue = true,
            valueType = isMulti ? "array" : "string",
            options = fieldDef.Options.Select(o => new GraphNodeOption { value = o.Value, label = o.Label }).ToList(),
        };
    }

    // ---- Helpers ----

    private async Task<CategoryMapping> LoadMappingAsync(int mappingId, CancellationToken ct)
    {
        var mapping = await _context.categoryMappings.FindAsync([mappingId], ct);
        if (mapping == null) throw new InvalidOperationException($"Mapping {mappingId} not found");
        return mapping;
    }

    private static MappingGraph ParseGraph(string? graphJson)
    {
        if (string.IsNullOrWhiteSpace(graphJson)) return new MappingGraph();
        return JsonSerializer.Deserialize<MappingGraph>(graphJson, _opts) ?? new MappingGraph();
    }

    private static string SerializeGraph(MappingGraph graph)
        => JsonSerializer.Serialize(graph, _opts);

    private static GraphNode? FindAdapterFieldsNode(MappingGraph graph)
        => graph.nodes.FirstOrDefault(n => n.type == "adapterFields");

    private static bool IsFieldConnected(AdapterField field, MappingGraph graph)
    {
        if (field.source != null) return true;
        return graph.edges.Any(e => e.targetHandle == $"f:{field.fieldKey}");
    }

    private static void AddModifierChain(MappingGraph graph, string sourceId, string sinkId,
        string fieldKey, List<AiModifierSpec>? modifiers)
    {
        var currentSource = sourceId;

        if (modifiers != null)
        {
            foreach (var mod in modifiers)
            {
                var modId = $"ai-mod-{fieldKey}-{Guid.NewGuid().ToString("N")[..8]}";
                graph.nodes.Add(new GraphNode
                {
                    id = modId, type = "modifier",
                    data = new GraphNodeData { modifierType = mod.Type, @params = mod.Params }
                });
                graph.edges.Add(new GraphEdge
                {
                    id = $"e-{currentSource}-{modId}",
                    source = currentSource, target = modId,
                    sourceHandle = "out", targetHandle = "in"
                });
                currentSource = modId;
            }
        }

        graph.edges.Add(new GraphEdge
        {
            id = $"e-{currentSource}-sink-{fieldKey}",
            source = currentSource, target = sinkId,
            sourceHandle = "out", targetHandle = $"f:{fieldKey}"
        });
    }

    // Builds a source node with the display metadata the editor expects (label + valueType) —
    // bare { attributeId } renders as "—" with pin type "any" in the frontend.
    private async Task<GraphNode> BuildSourceNodeAsync(
        string nodeId, string sourceType, string? sourceField, int? attributeId, CancellationToken ct)
    {
        switch (sourceType)
        {
            case "productField":
            {
                var info = Delegat.Services.Marketplace.MappingService.GetAvailableProductFields()
                    .FirstOrDefault(f => f.Key == sourceField);
                return new GraphNode
                {
                    id = nodeId, type = "productField",
                    data = new GraphNodeData
                    {
                        field = sourceField,
                        label = info?.DisplayName ?? sourceField,
                        valueType = info?.Type ?? "string",
                    }
                };
            }
            case "attribute":
            {
                var attr = attributeId.HasValue
                    ? await _context.categoryAttributes.AsNoTracking()
                        .Where(a => a.Id == attributeId.Value)
                        .Select(a => new { a.name, a.type })
                        .FirstOrDefaultAsync(ct)
                    : null;
                return new GraphNode
                {
                    id = nodeId, type = "attribute",
                    data = new GraphNodeData
                    {
                        attributeId = attributeId,
                        label = attr?.name ?? $"Атрибут #{attributeId}",
                        valueType = AttrValueType(attr?.type),
                    }
                };
            }
            case "storeAttribute":
            {
                var attr = attributeId.HasValue
                    ? await _context.storeAttributes.AsNoTracking()
                        .Where(a => a.Id == attributeId.Value)
                        .Select(a => new { a.name, a.type })
                        .FirstOrDefaultAsync(ct)
                    : null;
                return new GraphNode
                {
                    id = nodeId, type = "storeAttribute",
                    data = new GraphNodeData
                    {
                        attributeId = attributeId,
                        label = attr?.name ?? $"Атрибут #{attributeId}",
                        valueType = AttrValueType(attr?.type),
                    }
                };
            }
            default:
                return new GraphNode { id = nodeId, type = "static", data = new GraphNodeData { staticValue = "" } };
        }
    }

    // AttributeType → editor pin type ("any" is what the UI shows when unknown)
    private static string AttrValueType(Delegat.Enums.AttributeType? type) => type switch
    {
        Delegat.Enums.AttributeType.Number => "number",
        Delegat.Enums.AttributeType.Boolean => "boolean",
        Delegat.Enums.AttributeType.MultiSelect => "array",
        null => "any",
        _ => "string", // Text, Select, Barcode — single string value
    };
}

public class MappingCoverage
{
    public int Total { get; set; }
    public int Connected { get; set; }
    public int Unconnected { get; set; }
    public int RequiredTotal { get; set; }
    public int RequiredConnected { get; set; }
    public List<FieldInfo> UnmappedRequiredFields { get; set; } = new();
}

public class FieldState
{
    public string FieldKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "string";
    public bool IsRequired { get; set; }
    public bool Connected { get; set; }
    public string? Source { get; set; }
    public string? AliasOf { get; set; }
}

public class FieldInfo
{
    public string FieldKey { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsRequired { get; set; }
}

public class AiFieldSuggestion
{
    public string AdapterFieldKey { get; set; } = "";
    public string? SourceType { get; set; } // productField | attribute | storeAttribute | static | null
    public string? SourceField { get; set; }
    public int? SourceAttributeId { get; set; }
    public string? StaticValue { get; set; }
    public List<AiModifierSpec>? Modifiers { get; set; }
    public double Confidence { get; set; }
    public string? Reason { get; set; }
}

public class AiModifierSpec
{
    public string Type { get; set; } = "";
    public Dictionary<string, object>? Params { get; set; }
}

public class SuggestionApplyResult
{
    public int Applied { get; set; }
    public int AlreadyConnected { get; set; }
    public int NoSource { get; set; }
    public List<string> UnknownKeys { get; set; } = new();
    public List<string> InvalidModifiers { get; set; } = new();
}
