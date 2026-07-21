using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Enums;

namespace Delegat.Services.Marketplace.Graph;

/// <summary>
/// One field's mapping intent, decoupled from the (removed) FieldMapping entity. Used to generate /
/// refresh a mapping's node graph from a marketplace category schema (see <see cref="GraphGenerator"/>).
/// </summary>
public sealed class FieldSpec
{
    public required string externalFieldKey { get; set; }
    public string? externalFieldName { get; set; }
    public string? externalFieldType { get; set; }
    public bool isRequired { get; set; }
    public bool isEnabled { get; set; } = true;
    public int sortOrder { get; set; }
    public FieldMappingSourceType sourceType { get; set; }
    public string? sourceField { get; set; }
    public int? sourceAttributeId { get; set; }
    public string? modifiersJson { get; set; }
    public string? staticValue { get; set; }
}

/// <summary>
/// Generates a mapping's node graph (graphJson) from a flat field schema - the permanent C# port of the
/// frontend <c>autoMigrate</c>/<c>syncAdapterFields</c>. The graph is the single source of truth for a
/// category mapping (schema + per-field config + import/export), so this runs on category import.
/// Trivial 1:1 fields bind directly on the adapter (no node/wire); fields with modifiers get a source node
/// + modifier chain. Node positions are omitted - the frontend lays out position-less graphs on load.
/// </summary>
public static class GraphGenerator
{
    private static readonly JsonSerializerOptions SerializeOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ParseOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class ModDesc
    {
        public string? type { get; set; }
        public bool? enabled { get; set; }
        public Dictionary<string, object>? @params { get; set; }
    }

    /// <summary>
    /// Every mapping must expose a grouping field ("mapping as a translation layer"):
    /// field-based adapters (Ozon) already have a real one in their schema; endpoint-based
    /// adapters (WB) get a synthetic spec. Default binding is groupName 1:1 - the user can
    /// rewire it with modifiers to merge different local groups into one marketplace card.
    /// </summary>
    public static void EnsureGroupingField(List<FieldSpec> specs, IMarketplaceAdapter adapter)
    {
        var key = ProductSyncService.ResolveGroupingFieldKey(adapter);
        if (key == null) return;

        var existing = specs.FirstOrDefault(s => s.externalFieldKey == key);
        if (existing != null)
        {
            // Grouping field left unmapped by auto-mapping → default to groupName
            if (existing.sourceType == FieldMappingSourceType.Static &&
                existing.sourceAttributeId == null && string.IsNullOrEmpty(existing.staticValue))
            {
                existing.sourceType = FieldMappingSourceType.GroupName;
            }
            return;
        }

        if (key != ProductSyncService.SyntheticGroupFieldKey) return; // real field absent from schema - nothing to add

        specs.Add(new FieldSpec
        {
            externalFieldKey = key,
            externalFieldName = "Merge into card (group)",
            externalFieldType = "string",
            isRequired = false,
            isEnabled = true,
            sortOrder = specs.Count > 0 ? specs.Max(s => s.sortOrder) + 1 : 0,
            sourceType = FieldMappingSourceType.GroupName,
        });
    }

    /// <summary>Build a full graphJson from the field schema (used for a brand-new mapping).</summary>
    public static string BuildGraphJson(IEnumerable<FieldSpec> specs, IEnumerable<FieldSpec>? priceSpecs = null)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        var seq = 0;
        string NewId(string prefix) => $"{prefix}{seq++}";

        var cardFields = AppendSpecsToSink(
            specs.Where(f => f.isEnabled).OrderBy(f => f.sortOrder),
            "adapter-fields", nodes, edges, NewId);

        nodes.Add(new GraphNode
        {
            id = "adapter-fields",
            type = "adapterFields",
            data = new GraphNodeData { fields = cardFields },
        });

        var priceList = priceSpecs?.Where(f => f.isEnabled).OrderBy(f => f.sortOrder).ToList();
        if (priceList is { Count: > 0 })
        {
            var priceFields = AppendSpecsToSink(priceList, "adapter-price-fields", nodes, edges, NewId);
            nodes.Add(new GraphNode
            {
                id = "adapter-price-fields",
                type = "adapterPriceFields",
                data = new GraphNodeData { fields = priceFields },
            });
        }

        return JsonSerializer.Serialize(new MappingGraph { nodes = nodes, edges = edges }, SerializeOpts);
    }

    private static List<AdapterField> AppendSpecsToSink(
        IEnumerable<FieldSpec> specs,
        string sinkId,
        List<GraphNode> nodes,
        List<GraphEdge> edges,
        Func<string, string> newId)
    {
        var fields = new List<AdapterField>();
        foreach (var fm in specs)
        {
            var field = new AdapterField
            {
                fieldKey = fm.externalFieldKey,
                label = fm.externalFieldName ?? fm.externalFieldKey,
                outputType = fm.externalFieldType ?? "string",
                isRequired = fm.isRequired,
            };

            var mods = ParseActiveModifiers(fm.modifiersJson);

            if (mods.Count == 0)
            {
                var binding = DirectBinding(fm);
                if (binding != null)
                {
                    field.source = binding;
                    fields.Add(field);
                    continue;
                }
            }

            if (fm.sourceType == FieldMappingSourceType.Static && string.IsNullOrEmpty(fm.staticValue))
            {
                fields.Add(field);
                continue;
            }

            var source = MakeSourceNode(fm, newId);
            nodes.Add(source);
            var prevId = source.id;

            foreach (var mod in mods)
            {
                var modNode = new GraphNode
                {
                    id = newId("mod"),
                    type = "modifier",
                    data = new GraphNodeData
                    {
                        modifierType = mod.type,
                        @params = mod.@params ?? new Dictionary<string, object>(),
                    },
                };
                nodes.Add(modNode);
                edges.Add(MakeEdge(prevId, modNode.id));
                prevId = modNode.id;
            }

            fields.Add(field);
            edges.Add(MakeEdge(prevId, sinkId, $"f:{fm.externalFieldKey}"));
        }

        return fields;
    }

    /// <summary>
    /// Append marketplace fields added since the graph was generated (additive, never prunes user config).
    /// New fields bind directly when their source is a standard 1:1 source; otherwise they're appended
    /// unbound for the user to wire. Returns a fresh graph if there's no existing one.
    /// </summary>
    public static string SyncAdapterFields(string? existingGraphJson, IEnumerable<FieldSpec> specs)
    {
        MappingGraph? graph = null;
        if (!string.IsNullOrWhiteSpace(existingGraphJson))
        {
            try { graph = JsonSerializer.Deserialize<MappingGraph>(existingGraphJson, ParseOpts); }
            catch (JsonException) { graph = null; }
        }

        var adapter = graph?.nodes?.FirstOrDefault(n => n.type == "adapterFields");
        if (graph?.nodes == null || adapter == null)
            return BuildGraphJson(specs);

        adapter.data ??= new GraphNodeData();
        adapter.data.fields ??= new List<AdapterField>();
        var have = new HashSet<string>(adapter.data.fields
            .Where(f => !string.IsNullOrEmpty(f.fieldKey))
            .Select(f => f.fieldKey!));

        foreach (var fm in specs.Where(f => f.isEnabled).OrderBy(f => f.sortOrder))
        {
            if (string.IsNullOrEmpty(fm.externalFieldKey) || have.Contains(fm.externalFieldKey))
                continue;
            adapter.data.fields.Add(new AdapterField
            {
                fieldKey = fm.externalFieldKey,
                label = fm.externalFieldName ?? fm.externalFieldKey,
                outputType = fm.externalFieldType ?? "string",
                isRequired = fm.isRequired,
                source = ParseActiveModifiers(fm.modifiersJson).Count == 0 ? DirectBinding(fm) : null,
            });
            have.Add(fm.externalFieldKey);
        }

        return JsonSerializer.Serialize(graph, SerializeOpts);
    }

    /// <summary>
    /// Append price-API fields added since the graph was generated (additive, never prunes user config).
    /// </summary>
    public static string SyncPriceFields(string? existingGraphJson, IEnumerable<FieldSpec> specs)
    {
        MappingGraph? graph = null;
        if (!string.IsNullOrWhiteSpace(existingGraphJson))
        {
            try { graph = JsonSerializer.Deserialize<MappingGraph>(existingGraphJson, ParseOpts); }
            catch (JsonException) { graph = null; }
        }

        graph ??= new MappingGraph { nodes = new List<GraphNode>(), edges = new List<GraphEdge>() };
        graph.nodes ??= new List<GraphNode>();
        graph.edges ??= new List<GraphEdge>();

        var priceNode = graph.nodes.FirstOrDefault(n => n.type == "adapterPriceFields");
        if (priceNode == null)
        {
            priceNode = new GraphNode
            {
                id = "adapter-price-fields",
                type = "adapterPriceFields",
                data = new GraphNodeData { fields = new List<AdapterField>() },
            };
            graph.nodes.Add(priceNode);
        }

        priceNode.data ??= new GraphNodeData();
        priceNode.data.fields ??= new List<AdapterField>();
        var have = new HashSet<string>(priceNode.data.fields
            .Where(f => !string.IsNullOrEmpty(f.fieldKey))
            .Select(f => f.fieldKey!));

        foreach (var fm in specs.Where(f => f.isEnabled).OrderBy(f => f.sortOrder))
        {
            if (string.IsNullOrEmpty(fm.externalFieldKey) || have.Contains(fm.externalFieldKey))
                continue;
            priceNode.data.fields.Add(new AdapterField
            {
                fieldKey = fm.externalFieldKey,
                label = fm.externalFieldName ?? fm.externalFieldKey,
                outputType = fm.externalFieldType ?? "string",
                isRequired = fm.isRequired,
                source = ParseActiveModifiers(fm.modifiersJson).Count == 0 ? DirectBinding(fm) : null,
            });
            have.Add(fm.externalFieldKey);
        }

        return JsonSerializer.Serialize(graph, SerializeOpts);
    }

    private static AdapterFieldSource? DirectBinding(FieldSpec fm) => fm.sourceType switch
    {
        FieldMappingSourceType.CategoryAttribute when fm.sourceAttributeId != null =>
            new AdapterFieldSource { type = "attribute", attributeId = fm.sourceAttributeId },
        FieldMappingSourceType.StoreAttribute when fm.sourceAttributeId != null =>
            new AdapterFieldSource { type = "storeAttribute", attributeId = fm.sourceAttributeId },
        FieldMappingSourceType.ProductField when !string.IsNullOrEmpty(fm.sourceField) =>
            new AdapterFieldSource { type = "productField", field = fm.sourceField },
        FieldMappingSourceType.GroupName =>
            new AdapterFieldSource { type = "groupName" },
        _ => null,
    };

    private static GraphNode MakeSourceNode(FieldSpec fm, Func<string, string> newId)
    {
        var valueType = fm.externalFieldType ?? "any";
        var label = fm.externalFieldName ?? fm.sourceField ?? fm.externalFieldKey;
        return fm.sourceType switch
        {
            FieldMappingSourceType.ProductField => new GraphNode
            {
                id = newId("src"),
                type = "productField",
                data = new GraphNodeData { field = fm.sourceField, label = label, valueType = valueType },
            },
            FieldMappingSourceType.CategoryAttribute => new GraphNode
            {
                id = newId("src"),
                type = "attribute",
                data = new GraphNodeData { attributeId = fm.sourceAttributeId, label = label, valueType = valueType },
            },
            FieldMappingSourceType.StoreAttribute => new GraphNode
            {
                id = newId("src"),
                type = "storeAttribute",
                data = new GraphNodeData { attributeId = fm.sourceAttributeId, label = label, valueType = valueType },
            },
            FieldMappingSourceType.GroupName => new GraphNode
            {
                id = newId("grp"),
                type = "groupName",
                data = new GraphNodeData { label = "Group name", valueType = valueType },
            },
            _ => new GraphNode
            {
                id = newId("static"),
                type = "static",
                data = new GraphNodeData
                {
                    staticValue = fm.staticValue,
                    label = string.IsNullOrEmpty(fm.staticValue) ? "Static value" : fm.staticValue,
                    valueType = valueType,
                },
            },
        };
    }

    private static GraphEdge MakeEdge(string source, string target, string targetHandle = "in") => new()
    {
        id = targetHandle == "in" ? $"{source}->{target}" : $"{source}->{target}:{targetHandle}",
        source = source,
        target = target,
        sourceHandle = "out",
        targetHandle = targetHandle,
    };

    private static List<ModDesc> ParseActiveModifiers(string? modifiersJson)
    {
        if (string.IsNullOrWhiteSpace(modifiersJson))
            return new List<ModDesc>();
        try
        {
            var list = JsonSerializer.Deserialize<List<ModDesc>>(modifiersJson, ParseOpts);
            return (list ?? new List<ModDesc>())
                .Where(m => m != null && m.enabled != false && !string.IsNullOrEmpty(m.type))
                .ToList();
        }
        catch (JsonException)
        {
            return new List<ModDesc>();
        }
    }
}
