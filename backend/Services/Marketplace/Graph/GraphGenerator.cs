using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Enums;
using Delegat.Services.Marketplace;

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
    public string? externalFieldGroup { get; set; }
    public string? externalFieldDescription { get; set; }
    public string? externalFieldUnit { get; set; }
    public string? externalFieldUnitCode { get; set; }
    public string? externalFieldUnitId { get; set; }
    public bool isRequired { get; set; }
    public bool isEnabled { get; set; } = true;
    public int sortOrder { get; set; }
    public FieldMappingSourceType sourceType { get; set; }
    public string? sourceField { get; set; }
    public int? sourceAttributeId { get; set; }
    public string? modifiersJson { get; set; }
    public string? staticValue { get; set; }
    /// <summary>
    /// When set, this field is an alias of another adapter field (by <see cref="externalFieldKey"/>).
    /// Export copies the primary's value; import ignores the alias.
    /// </summary>
    public string? aliasOf { get; set; }
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

        var cardSpecs = AliasDuplicateProductFieldSources(
            specs.Where(f => f.isEnabled).ToList());
        var cardFields = AppendSpecsToSink(
            cardSpecs.OrderBy(f => f.sortOrder),
            "adapter-fields", nodes, edges, NewId);

        nodes.Add(new GraphNode
        {
            id = "adapter-fields",
            type = "adapterFields",
            data = new GraphNodeData { fields = cardFields },
        });

        var priceList = AliasDuplicateProductFieldSources(
            priceSpecs?.Where(f => f.isEnabled).ToList() ?? []);
        if (priceList is { Count: > 0 })
        {
            var priceFields = AppendSpecsToSink(priceList.OrderBy(f => f.sortOrder), "adapter-price-fields", nodes, edges, NewId);
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
                unit = fm.externalFieldUnit,
                unitCode = fm.externalFieldUnitCode,
                unitId = fm.externalFieldUnitId,
                label = fm.externalFieldName ?? fm.externalFieldKey,
                outputType = fm.externalFieldType ?? "string",
                group = fm.externalFieldGroup,
                description = fm.externalFieldDescription,
                sortOrder = fm.sortOrder,
                isRequired = fm.isRequired,
            };

            if (!string.IsNullOrEmpty(fm.aliasOf))
            {
                field.aliasOf = fm.aliasOf;
                fields.Add(field);
                continue;
            }

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
                        collapsed = true,
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

        var incoming = AliasDuplicateProductFieldSources(
            specs.Where(f => f.isEnabled).ToList());
        foreach (var fm in incoming.OrderBy(f => f.sortOrder))
        {
            if (string.IsNullOrEmpty(fm.externalFieldKey))
                continue;
            var existing = adapter.data.fields.FirstOrDefault(f =>
                string.Equals(f.fieldKey, fm.externalFieldKey, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                // Refresh schema metadata while preserving the operator's binding and wiring.
                existing.unit = fm.externalFieldUnit;
                existing.unitCode = fm.externalFieldUnitCode;
                existing.unitId = fm.externalFieldUnitId;
                continue;
            }
            adapter.data.fields.Add(new AdapterField
            {
                fieldKey = fm.externalFieldKey,
                unit = fm.externalFieldUnit,
                unitCode = fm.externalFieldUnitCode,
                unitId = fm.externalFieldUnitId,
                label = fm.externalFieldName ?? fm.externalFieldKey,
                outputType = fm.externalFieldType ?? "string",
                isRequired = fm.isRequired,
                aliasOf = fm.aliasOf,
                source = string.IsNullOrEmpty(fm.aliasOf) && ParseActiveModifiers(fm.modifiersJson).Count == 0
                    ? DirectBinding(fm)
                    : null,
            });
            have.Add(fm.externalFieldKey);
        }

        return JsonSerializer.Serialize(graph, SerializeOpts);
    }

    /// <summary>
    /// Append price-API fields added since the graph was generated.
    /// If the same keys still sit on adapterFields (legacy), migrate their bindings into the price node and remove the duplicates.
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
        var adapter = graph.nodes.FirstOrDefault(n => n.type == "adapterFields");
        if (adapter != null)
        {
            adapter.data ??= new GraphNodeData();
            adapter.data.fields ??= new List<AdapterField>();
        }

        var incoming = AliasDuplicateProductFieldSources(
            specs.Where(f => f.isEnabled).ToList());
        var incomingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fm in incoming.OrderBy(f => f.sortOrder))
        {
            if (string.IsNullOrEmpty(fm.externalFieldKey))
                continue;
            incomingKeys.Add(fm.externalFieldKey);

            AdapterFieldSource? migratedSource = null;
            string? migratedLabel = null;
            string? migratedType = null;

            if (adapter?.data?.fields is { Count: > 0 })
            {
                var legacy = adapter.data.fields.FirstOrDefault(f =>
                    string.Equals(f.fieldKey, fm.externalFieldKey, StringComparison.OrdinalIgnoreCase));
                if (legacy != null)
                {
                    migratedSource = legacy.source;
                    migratedLabel = legacy.label;
                    migratedType = legacy.outputType;
                    adapter.data.fields.Remove(legacy);

                    // Drop edges that targeted the card-field sink for this key
                    var sinkId = adapter.id ?? "adapter-fields";
                    var handle = $"f:{fm.externalFieldKey}";
                    graph.edges.RemoveAll(e =>
                        string.Equals(e.target, sinkId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.targetHandle, handle, StringComparison.OrdinalIgnoreCase));
                }
            }

            var existingField = priceNode.data.fields.FirstOrDefault(f =>
                string.Equals(f.fieldKey, fm.externalFieldKey, StringComparison.OrdinalIgnoreCase));
            if (existingField != null)
            {
                // Prefer a migrated binding when the price field is still unbound
                if (migratedSource != null)
                {
                    if (existingField.source == null)
                        existingField.source = migratedSource;
                }

                // Marketplace metadata is authoritative; keep the user's binding, alias, and omit state.
                existingField.label = fm.externalFieldName ?? fm.externalFieldKey;
                existingField.outputType = fm.externalFieldType ?? "string";
                existingField.group = fm.externalFieldGroup;
                existingField.description = fm.externalFieldDescription;
                existingField.unit = fm.externalFieldUnit;
                existingField.unitCode = fm.externalFieldUnitCode;
                existingField.unitId = fm.externalFieldUnitId;
                existingField.sortOrder = fm.sortOrder;
                existingField.isRequired = fm.isRequired;
                continue;
            }

            priceNode.data.fields.Add(new AdapterField
            {
                fieldKey = fm.externalFieldKey,
                unit = fm.externalFieldUnit,
                unitCode = fm.externalFieldUnitCode,
                unitId = fm.externalFieldUnitId,
                label = fm.externalFieldName ?? migratedLabel ?? fm.externalFieldKey,
                outputType = fm.externalFieldType ?? migratedType ?? "string",
                group = fm.externalFieldGroup,
                description = fm.externalFieldDescription,
                sortOrder = fm.sortOrder,
                isRequired = fm.isRequired,
                aliasOf = fm.aliasOf,
                source = migratedSource ?? (string.IsNullOrEmpty(fm.aliasOf)
                    && ParseActiveModifiers(fm.modifiersJson).Count == 0
                    ? DirectBinding(fm)
                    : null),
            });
        }

        var dropped = priceNode.data.fields
            .Where(f => !string.IsNullOrEmpty(f.fieldKey)
                && CategoryImportService.IsStoreExtendedPriceKey(f.fieldKey)
                && !incomingKeys.Contains(f.fieldKey!))
            .Select(f => f.fieldKey!)
            .ToList();
        if (dropped.Count > 0)
        {
            priceNode.data.fields.RemoveAll(f =>
                f.fieldKey != null && dropped.Contains(f.fieldKey, StringComparer.OrdinalIgnoreCase));
            var priceId = priceNode.id ?? "adapter-price-fields";
            graph.edges.RemoveAll(e =>
                string.Equals(e.target, priceId, StringComparison.OrdinalIgnoreCase)
                && e.targetHandle != null
                && dropped.Any(k => string.Equals(e.targetHandle, "f:" + k, StringComparison.OrdinalIgnoreCase)));
            if (adapter?.data?.fields is { Count: > 0 })
            {
                adapter.data.fields.RemoveAll(f =>
                    f.fieldKey != null && dropped.Contains(f.fieldKey, StringComparer.OrdinalIgnoreCase));
            }
        }

        var schemaOrder = incoming
            .OrderBy(field => field.sortOrder)
            .Select((field, index) => (key: field.externalFieldKey, index))
            .Where(item => !string.IsNullOrEmpty(item.key))
            .ToDictionary(item => item.key!, item => item.index, StringComparer.OrdinalIgnoreCase);
        priceNode.data.fields = priceNode.data.fields
            .Select((field, index) => (field, index))
            .OrderBy(item => item.field.fieldKey != null && schemaOrder.ContainsKey(item.field.fieldKey) ? 0 : 1)
            .ThenBy(item => item.field.fieldKey != null && schemaOrder.TryGetValue(item.field.fieldKey, out var order)
                ? order
                : item.index)
            .Select(item => item.field)
            .ToList();

        return JsonSerializer.Serialize(graph, SerializeOpts);
    }

    /// <summary>
    /// When several adapter fields auto-bind to the same product field (e.g. <c>name</c> and
    /// <c>product_name</c>), keep one primary on that product field and merge the rest via
    /// <see cref="FieldSpec.aliasOf"/> ("same as another adapter field").
    /// </summary>
    internal static List<FieldSpec> AliasDuplicateProductFieldSources(List<FieldSpec> specs)
    {
        var groups = specs
            .Where(s =>
                string.IsNullOrEmpty(s.aliasOf) &&
                s.sourceType == FieldMappingSourceType.ProductField &&
                !string.IsNullOrEmpty(s.sourceField))
            .GroupBy(s => s.sourceField!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var members = group.ToList();
            if (members.Count < 2)
                continue;

            var primary = PickPrimaryProductField(members);
            foreach (var other in members)
            {
                if (ReferenceEquals(other, primary))
                    continue;
                other.aliasOf = primary.externalFieldKey;
                other.sourceType = FieldMappingSourceType.Static;
                other.sourceField = null;
                other.sourceAttributeId = null;
                other.staticValue = null;
            }
        }

        return specs;
    }

    private static FieldSpec PickPrimaryProductField(List<FieldSpec> members)
    {
        var target = members[0].sourceField ?? "";
        return members
            .OrderByDescending(m =>
                string.Equals(m.externalFieldKey, target, StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.sortOrder)
            .ThenBy(m => m.externalFieldKey.Length)
            .ThenBy(m => m.externalFieldKey, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static AdapterFieldSource? DirectBinding(FieldSpec fm)
    {
        if (!string.IsNullOrEmpty(fm.aliasOf))
            return null;
        return fm.sourceType switch
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
    }

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
                data = new GraphNodeData { field = fm.sourceField, label = label, valueType = valueType, collapsed = true },
            },
            FieldMappingSourceType.CategoryAttribute => new GraphNode
            {
                id = newId("src"),
                type = "attribute",
                data = new GraphNodeData { attributeId = fm.sourceAttributeId, label = label, valueType = valueType, collapsed = true },
            },
            FieldMappingSourceType.StoreAttribute => new GraphNode
            {
                id = newId("src"),
                type = "storeAttribute",
                data = new GraphNodeData { attributeId = fm.sourceAttributeId, label = label, valueType = valueType, collapsed = true },
            },
            FieldMappingSourceType.GroupName => new GraphNode
            {
                id = newId("grp"),
                type = "groupName",
                data = new GraphNodeData { label = "Group name", valueType = valueType, collapsed = true },
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
                    collapsed = true,
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
