using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Enums;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Mapping;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Services.Ai;

public class GraphMutationService
{
    private const string CardSinkType = "adapterFields";
    private const string PriceSinkType = "adapterPriceFields";

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
    private static AdapterField ResolveField(GraphNode sinkNode, string adapterFieldKey,
        ExternalField? fieldDef, string sinkKind = "card")
    {
        var field = sinkNode.data!.fields!.FirstOrDefault(f => f.fieldKey == adapterFieldKey);
        if (field != null)
        {
            if (!string.IsNullOrEmpty(field.aliasOf))
                throw new InvalidOperationException(
                    $"Field '{adapterFieldKey}' is merged with '{field.aliasOf}' — connect '{field.aliasOf}' or call unmerge_adapter_fields first");
            return field;
        }

        if (fieldDef == null)
        {
            var hint = sinkKind == "price" ? "get_price_fields" : "get_external_fields";
            throw new InvalidOperationException(
                $"Field '{adapterFieldKey}' not found — check the key via {hint}");
        }

        field = new AdapterField
        {
            fieldKey = fieldDef.Key,
            label = fieldDef.Name,
            outputType = fieldDef.Type,
            isRequired = fieldDef.IsRequired,
        };
        sinkNode.data!.fields!.Add(field);
        return field;
    }

    // Connect a field directly (AdapterFieldSource — no intermediate nodes).
    // sink: "card" (default) | "price" — price-API block (Ozon/WB dedicated price fields).
    public async Task ConnectFieldAsync(int mappingId, string adapterFieldKey,
        string sourceType, string? sourceField, int? sourceAttributeId,
        string? sink = null, ExternalField? fieldDef = null, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var (sinkNode, sinkKind) = GetSink(graph, sink, adapterFieldKey);
        var field = ResolveField(sinkNode, adapterFieldKey, fieldDef, sinkKind);

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
    public async Task DisconnectFieldAsync(int mappingId, string adapterFieldKey,
        string? sink = null, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var (sinkNode, _) = GetSink(graph, sink, adapterFieldKey);
        var field = sinkNode.data!.fields!.FirstOrDefault(f => f.fieldKey == adapterFieldKey);
        if (field != null)
        {
            field.source = null;
            graph.edges.RemoveAll(e =>
                e.target == sinkNode.id && e.targetHandle == $"f:{adapterFieldKey}");
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    // Set a static value for a field (creates static node + edge, removes direct source).
    // When fieldDef with options is provided, creates a "Static value" node instead:
    // value is resolved against the adapter's predefined options (by value or label).
    public async Task SetStaticValueAsync(int mappingId, string adapterFieldKey, string value,
        string? sink = null, ExternalField? fieldDef = null, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var (sinkNode, sinkKind) = GetSink(graph, sink, adapterFieldKey);
        var field = ResolveField(sinkNode, adapterFieldKey, fieldDef, sinkKind);

        // Clear direct source
        field.source = null;
        graph.edges.RemoveAll(e =>
            e.target == sinkNode.id && e.targetHandle == $"f:{adapterFieldKey}");

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
        string? sink = null, ExternalField? fieldDef = null, CancellationToken ct = default)
    {
        if (!ModifierPipeline.IsKnownModifier(modifierType))
            throw new InvalidOperationException(
                $"Modifier '{modifierType}' does not exist. Allowed types: {string.Join(", ", ModifierPipeline.KnownModifierTypes)}");

        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var (sinkNode, sinkKind) = GetSink(graph, sink, adapterFieldKey);
        var field = ResolveField(sinkNode, adapterFieldKey, fieldDef, sinkKind);

        var existingEdge = graph.edges.FirstOrDefault(e =>
            e.target == sinkNode.id && e.targetHandle == $"f:{adapterFieldKey}");
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

    // Analyze coverage of card fields (legacy — card sink only)
    public async Task<MappingCoverage> GetCoverageAsync(int mappingId, CancellationToken ct = default)
    {
        var report = await GetCoverageReportAsync(mappingId, ct);
        return report.Card;
    }

    public async Task<MappingCoverageReport> GetCoverageReportAsync(int mappingId, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var cardNode = FindSinkNode(graph, CardSinkType);
        var priceNode = FindSinkNode(graph, PriceSinkType);

        return new MappingCoverageReport
        {
            Card = BuildCoverage(cardNode, graph),
            Price = priceNode?.data?.fields is { Count: > 0 }
                ? BuildCoverage(priceNode, graph)
                : null,
        };
    }

    private static MappingCoverage BuildCoverage(GraphNode? sinkNode, MappingGraph graph)
    {
        var fields = sinkNode?.data?.fields ?? [];
        var total = fields.Count;
        var connected = sinkNode == null ? 0 : fields.Count(f => IsFieldConnected(f, graph, sinkNode));
        var requiredTotal = fields.Count(f => f.isRequired);
        var requiredConnected = sinkNode == null
            ? 0
            : fields.Count(f => f.isRequired && IsFieldConnected(f, graph, sinkNode));
        var unmappedRequired = sinkNode == null
            ? []
            : fields.Where(f => f.isRequired && !IsFieldConnected(f, graph, sinkNode)).ToList();

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
        var (sinkNode, _) = GetSink(graph, "card", null);
        if (sinkNode.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");

        var byKey = sinkNode.data.fields
            .Where(f => !string.IsNullOrEmpty(f.fieldKey))
            .ToDictionary(f => f.fieldKey!);

        if (!byKey.TryGetValue(primaryFieldKey, out var primary))
            throw new InvalidOperationException($"Field '{primaryFieldKey}' not found in the mapping");
        if (!string.IsNullOrEmpty(primary.aliasOf))
            throw new InvalidOperationException($"Field '{primaryFieldKey}' is itself an alias of '{primary.aliasOf}' — unmerge it first");

        foreach (var key in aliasFieldKeys)
        {
            if (key == primaryFieldKey)
                throw new InvalidOperationException("A field cannot be an alias of itself");
            if (!byKey.TryGetValue(key, out var alias))
                throw new InvalidOperationException($"Field '{key}' not found in the mapping");
            if (byKey.Values.Any(f => f.aliasOf == key))
                throw new InvalidOperationException($"Field '{key}' is the primary for other aliases — unmerge them first");

            alias.aliasOf = primaryFieldKey;
            // Remove the alias's own connections — its value now comes from the primary
            alias.source = null;
            graph.edges.RemoveAll(e =>
                e.target == sinkNode.id && e.targetHandle == $"f:{key}");
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UnmergeFieldsAsync(int mappingId, List<string> fieldKeys, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var (sinkNode, _) = GetSink(graph, "card", null);
        if (sinkNode?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");

        foreach (var key in fieldKeys)
        {
            var field = sinkNode.data.fields.FirstOrDefault(f => f.fieldKey == key);
            if (field == null)
                throw new InvalidOperationException($"Field '{key}' not found in the mapping");
            field.aliasOf = null;
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
    }

    // Per-field connection state for the AI (card + price sinks).
    public async Task<List<FieldState>> GetFieldStatesAsync(int mappingId, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var states = new List<FieldState>();

        var cardNode = FindSinkNode(graph, CardSinkType);
        if (cardNode?.data?.fields != null)
            states.AddRange(BuildFieldStates(cardNode, graph, "card"));

        var priceNode = FindSinkNode(graph, PriceSinkType);
        if (priceNode?.data?.fields is { Count: > 0 })
            states.AddRange(BuildFieldStates(priceNode, graph, "price"));

        return states;
    }

    private static List<FieldState> BuildFieldStates(GraphNode sinkNode, MappingGraph graph, string sinkKind)
    {
        var fields = sinkNode.data?.fields ?? [];
        var connectedKeys = new HashSet<string>(fields
            .Where(f => f.fieldKey != null && IsFieldConnected(f, graph, sinkNode))
            .Select(f => f.fieldKey!));

        var states = new List<FieldState>();
        foreach (var f in fields)
        {
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
                    SinkType = sinkKind,
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
                    "groupName" => "groupName",
                    _ => f.source.type,
                };
            }
            else
            {
                var edge = graph.edges.FirstOrDefault(e =>
                    e.target == sinkNode.id && e.targetHandle == $"f:{f.fieldKey}");
                if (edge != null)
                {
                    var srcNode = graph.nodes.FirstOrDefault(n => n.id == edge.source);
                    source = srcNode?.type switch
                    {
                        "static" => $"static:\"{srcNode.data?.staticValue}\"",
                        "productField" => $"productField:{srcNode.data?.field}",
                        "attribute" => $"attribute:{srcNode.data?.attributeId}",
                        "storeAttribute" => $"storeAttribute:{srcNode.data?.attributeId}",
                        _ => "chain",
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
                SinkType = sinkKind,
            });
        }
        return states;
    }

    // Apply a list of suggestions to the graph
    public async Task<SuggestionApplyResult> ApplySuggestionsAsync(int mappingId,
        IReadOnlyList<AiFieldSuggestion> suggestions,
        string? sink = null,
        IReadOnlyCollection<Delegat.Models.Marketplace.DTOs.ExternalField>? adapterFieldDefs = null,
        CancellationToken ct = default)
    {
        var result = new SuggestionApplyResult();
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var (sinkNode, sinkKind) = GetSink(graph, sink ?? "card", null);
        if (sinkNode.data?.fields == null) return result;

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
                field = ResolveField(sinkNode, suggestion.AdapterFieldKey!, def, sinkKind);
            }
            // Alias fields take the primary's value — connecting them separately would undo the merge
            if (!string.IsNullOrEmpty(field.aliasOf)) { result.AlreadyConnected++; continue; }
            if (IsFieldConnected(field, graph, sinkNode)) { result.AlreadyConnected++; continue; }

            if (suggestion.SourceType == null) { result.NoSource++; continue; }

            if (string.Equals(sinkKind, "price", StringComparison.OrdinalIgnoreCase))
            {
                var priceDef = adapterFieldDefs?.FirstOrDefault(d => d.Key == suggestion.AdapterFieldKey);
                if (priceDef != null)
                {
                    var attrs = await _context.categoryAttributes.AsNoTracking()
                        .Where(a => a.categoryId == mapping.localCategoryId)
                        .ToListAsync(ct);
                    var attributeMap = BuildAttributeMap(attrs);
                    if (!CategoryImportService.IsValidPriceBinding(
                            priceDef, suggestion.SourceType, suggestion.SourceField,
                            suggestion.SourceAttributeId, attributeMap))
                    {
                        result.NoSource++;
                        continue;
                    }
                }
            }

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
                // Prefer a "Static value" node bound to adapter options over a free-text constant
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

    /// <summary>
    /// Deterministic price-field wiring (productField / category attrs / static discount).
    /// </summary>
    public async Task<DeterministicPriceMapResult> ApplyDeterministicPriceMappingsAsync(
        int mappingId,
        IReadOnlyCollection<ExternalField> priceFieldDefs,
        CancellationToken ct = default)
    {
        var result = new DeterministicPriceMapResult();
        if (priceFieldDefs.Count == 0) return result;

        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var sinkNode = EnsurePriceSink(graph);
        sinkNode.data ??= new GraphNodeData();
        sinkNode.data.fields ??= new List<AdapterField>();

        var attrs = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == mapping.localCategoryId)
            .ToListAsync(ct);
        var attributeMap = attrs
            .GroupBy(a => a.key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        foreach (var a in attrs)
        {
            if (!string.IsNullOrWhiteSpace(a.name) && !attributeMap.ContainsKey(a.name))
                attributeMap[a.name] = a.Id;
        }

        var extendedPrice = await (
            from m in _context.categoryMappings.AsNoTracking()
            join c in _context.marketplaceConnections.AsNoTracking() on m.connectionId equals c.Id
            join s in _context.stores.AsNoTracking() on c.storeId equals s.Id
            where m.Id == mappingId
            select s.extendedPriceParameters
        ).FirstOrDefaultAsync(ct);

        foreach (var def in priceFieldDefs.OrderBy(f => f.SortOrder))
        {
            if (def.Key.Equals("discountedPrice", StringComparison.OrdinalIgnoreCase))
                continue;

            var field = sinkNode.data.fields.FirstOrDefault(f => f.fieldKey == def.Key);
            if (field == null)
                field = ResolveField(sinkNode, def.Key, def, "price");

            // Promote legacy wire bindings to direct pill (same as initial graph import).
            TryPromoteDirectWireToPill(field, graph, sinkNode);

            // Drop invalid bindings (e.g. minPrice -> "Old price" attribute).
            if (!IsValidPriceFieldBinding(field, graph, sinkNode, def, attributeMap))
                ClearFieldBinding(field, graph, sinkNode);

            if (IsFieldConnected(field, graph, sinkNode))
            {
                result.AlreadyConnected++;
                continue;
            }

            var (sourceType, sourceField, sourceAttrId) =
                CategoryImportService.DeterminePriceAutoMapping(def, attributeMap, extendedPrice);

            if (sourceType == FieldMappingSourceType.Static)
            {
                if (def.Key.Equals("discount", StringComparison.OrdinalIgnoreCase) && !extendedPrice)
                {
                    var nodeId = $"price-static-{def.Key}-{Guid.NewGuid().ToString("N")[..8]}";
                    graph.nodes.Add(new GraphNode
                    {
                        id = nodeId, type = "static",
                        data = new GraphNodeData { staticValue = "0", label = "0", valueType = "number" },
                    });
                    graph.edges.Add(new GraphEdge
                    {
                        id = $"e-{nodeId}-price-{def.Key}",
                        source = nodeId, target = sinkNode.id,
                        sourceHandle = "out", targetHandle = $"f:{def.Key}",
                    });
                    result.Connected++;
                }
                else
                {
                    result.Skipped++;
                }
                continue;
            }

            field.source = sourceType switch
            {
                FieldMappingSourceType.ProductField => new AdapterFieldSource
                {
                    type = "productField",
                    field = sourceField,
                },
                FieldMappingSourceType.CategoryAttribute => new AdapterFieldSource
                {
                    type = "attribute",
                    attributeId = sourceAttrId,
                },
                FieldMappingSourceType.StoreAttribute => new AdapterFieldSource
                {
                    type = "storeAttribute",
                    attributeId = sourceAttrId,
                },
                _ => null,
            };
            graph.edges.RemoveAll(e =>
                e.target == sinkNode.id && e.targetHandle == $"f:{def.Key}");
            result.Connected++;
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>
    /// Ensures every connected select field fed by a category attribute has a full optionMap.
    /// </summary>
    public async Task<EnsureValueMapsResult> EnsureSelectValueMapsAsync(
        int mappingId,
        IReadOnlyCollection<ExternalField>? cardFieldDefs = null,
        IReadOnlyCollection<ExternalField>? priceFieldDefs = null,
        CancellationToken ct = default)
    {
        var result = new EnsureValueMapsResult();
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var attrs = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == mapping.localCategoryId)
            .ToListAsync(ct);
        var attrById = attrs.ToDictionary(a => a.Id);

        var fieldDefs = new Dictionary<string, ExternalField>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in cardFieldDefs ?? [])
            fieldDefs[f.Key] = f;
        foreach (var f in priceFieldDefs ?? [])
            fieldDefs[f.Key] = f;

        foreach (var (sinkType, sinkKind) in new[]
                 { (CardSinkType, "card"), (PriceSinkType, "price") })
        {
            var sink = FindSinkNode(graph, sinkType);
            if (sink?.data?.fields == null) continue;

            foreach (var edge in graph.edges.Where(e =>
                         e.target == sink.id && e.targetHandle?.StartsWith("f:") == true))
            {
                var fieldKey = edge.targetHandle![3..];
                if (!fieldDefs.TryGetValue(fieldKey, out var fieldDef)) continue;
                if (fieldDef.Options is not { Count: > 0 }) continue;

                var chain = TraceSourceChain(graph, edge.source);
                if (chain.AttributeId is not int attrId || !attrById.TryGetValue(attrId, out var attr))
                    continue;
                if (attr.type is not (AttributeType.Select or AttributeType.MultiSelect)) continue;

                var localOpts = OptionAlignmentHelper.ParseAttributeOptions(attr.options);
                if (localOpts.Count == 0) continue;
                if (!OptionAlignmentHelper.NeedsValueMap(localOpts, fieldDef.Options)) continue;

                var fullMap = OptionAlignmentHelper.NormalizeValueMapMappings(
                    OptionAlignmentHelper.BuildValueMapEntries(localOpts, fieldDef.Options),
                    localOpts, fieldDef.Options);

                if (fullMap.Count == 0) continue;

                var valueMapNode = chain.ModifierNodes.LastOrDefault(n =>
                    n.data?.modifierType is "valueMap" or "optionMap");

                if (valueMapNode != null)
                {
                    valueMapNode.data ??= new GraphNodeData();
                    valueMapNode.data.modifierType = "optionMap";
                    valueMapNode.data.targetFieldKey = fieldKey;
                    valueMapNode.data.targetFieldOptions = fieldDef.Options
                        .Select(o => new GraphNodeOption { value = o.Value, label = o.Label })
                        .ToList();
                    valueMapNode.data.targetValueType = fieldDef.Type;
                    valueMapNode.data.@params ??= new Dictionary<string, object>();
                    var existing = ParseMappings(valueMapNode.data.@params);
                    var merged = OptionAlignmentHelper.MergeValueMapMappings(existing, fullMap);
                    valueMapNode.data.@params["mappings"] = merged;
                    result.Updated++;
                    result.AddedMappings += merged.Count - existing.Count;
                }
                else
                {
                    var modId = $"ai-optionmap-{fieldKey}-{Guid.NewGuid().ToString("N")[..8]}";
                    var modNode = new GraphNode
                    {
                        id = modId, type = "modifier",
                        data = new GraphNodeData
                        {
                            modifierType = "optionMap",
                            targetFieldKey = fieldKey,
                            targetFieldOptions = fieldDef.Options
                                .Select(o => new GraphNodeOption { value = o.Value, label = o.Label })
                                .ToList(),
                            targetValueType = fieldDef.Type,
                            @params = new Dictionary<string, object> { ["mappings"] = fullMap },
                        },
                    };
                    graph.nodes.Add(modNode);

                    var tailId = chain.TailNodeId;
                    graph.edges.RemoveAll(e => e.source == tailId && e.target == sink.id &&
                                               e.targetHandle == $"f:{fieldKey}");
                    graph.edges.Add(new GraphEdge
                    {
                        id = $"e-{tailId}-{modId}",
                        source = tailId, target = modId,
                        sourceHandle = "out", targetHandle = "in",
                    });
                    graph.edges.Add(new GraphEdge
                    {
                        id = $"e-{modId}-sink-{fieldKey}",
                        source = modId, target = sink.id,
                        sourceHandle = "out", targetHandle = $"f:{fieldKey}",
                    });
                    result.Inserted++;
                    result.AddedMappings += fullMap.Count;
                }
            }
        }

        if (result.Updated > 0 || result.Inserted > 0)
        {
            mapping.graphJson = SerializeGraph(graph);
            await _context.SaveChangesAsync(ct);
        }

        return result;
    }

    private sealed record SourceChainTrace(
        int? AttributeId,
        string TailNodeId,
        List<GraphNode> ModifierNodes);

    private static SourceChainTrace TraceSourceChain(MappingGraph graph, string startId)
    {
        var byId = graph.nodes.ToDictionary(n => n.id);
        var incoming = graph.edges.GroupBy(e => e.target)
            .ToDictionary(g => g.Key, g => g.ToList());

        int? attrId = null;
        var modifiers = new List<GraphNode>();
        var current = startId;
        var seen = new HashSet<string>();

        while (seen.Add(current) && byId.TryGetValue(current, out var node))
        {
            if (node.type == "attribute" && node.data?.attributeId is int id)
                attrId = id;
            else if (node.type == "modifier")
                modifiers.Add(node);

            var ins = incoming.GetValueOrDefault(current);
            if (ins is not { Count: > 0 }) break;
            current = ins[0].source;
        }

        return new SourceChainTrace(attrId, startId, modifiers);
    }

    private static List<Dictionary<string, string>> ParseMappings(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("mappings", out var raw) || raw == null)
            return [];

        if (raw is JsonElement el && el.ValueKind == JsonValueKind.Array)
        {
            return el.EnumerateArray().Select(item =>
            {
                var from = item.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";
                var to = item.TryGetProperty("to", out var t) ? t.GetString() ?? "" : "";
                return new Dictionary<string, string> { ["from"] = from, ["to"] = to };
            }).ToList();
        }

        return [];
    }

    // Builds node data for a static value. If the adapter field has predefined options,
    // produces a "Static value" node: staticValue = option value(s), label = option label(s).
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
                    $"Value \"{input}\" is not among the allowed options for field \"{fieldDef.Name}\". Available: {sample}" +
                    (fieldDef.Options.Count > 15 ? $" and {fieldDef.Options.Count - 15} more" : ""));
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

    private static GraphNode? FindSinkNode(MappingGraph graph, string sinkType)
        => graph.nodes.FirstOrDefault(n => n.type == sinkType);

    private static GraphNode EnsurePriceSink(MappingGraph graph)
    {
        var node = FindSinkNode(graph, PriceSinkType);
        if (node != null) return node;
        node = new GraphNode
        {
            id = "adapter-price-fields",
            type = PriceSinkType,
            data = new GraphNodeData { fields = new List<AdapterField>() },
        };
        graph.nodes.Add(node);
        return node;
    }

    private static (GraphNode sink, string sinkKind) GetSink(
        MappingGraph graph, string? sinkHint, string? fieldKey = null)
    {
        var card = FindSinkNode(graph, CardSinkType);
        var price = FindSinkNode(graph, PriceSinkType);

        if (string.Equals(sinkHint, "price", StringComparison.OrdinalIgnoreCase))
        {
            price = EnsurePriceSink(graph);
            price.data ??= new GraphNodeData();
            price.data.fields ??= new List<AdapterField>();
            return (price, "price");
        }

        if (string.Equals(sinkHint, "card", StringComparison.OrdinalIgnoreCase))
        {
            if (card?.data?.fields == null)
                throw new InvalidOperationException("adapterFields node not found in graph");
            card.data.fields ??= new List<AdapterField>();
            return (card, "card");
        }

        if (!string.IsNullOrEmpty(fieldKey))
        {
            var inCard = card?.data?.fields?.Any(f => f.fieldKey == fieldKey) == true;
            var inPrice = price?.data?.fields?.Any(f => f.fieldKey == fieldKey) == true;
            if (inPrice && !inCard)
            {
                price!.data!.fields ??= new List<AdapterField>();
                return (price, "price");
            }
        }

        if (card?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");
        card.data.fields ??= new List<AdapterField>();
        return (card, "card");
    }

    private static bool IsFieldConnected(AdapterField field, MappingGraph graph, GraphNode sinkNode)
    {
        if (field.source != null) return true;
        return graph.edges.Any(e =>
            e.target == sinkNode.id && e.targetHandle == $"f:{field.fieldKey}");
    }

    private static Dictionary<string, int> BuildAttributeMap(List<CategoryAttribute> attrs)
    {
        var attributeMap = attrs
            .GroupBy(a => a.key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        foreach (var a in attrs)
        {
            if (!string.IsNullOrWhiteSpace(a.name) && !attributeMap.ContainsKey(a.name))
                attributeMap[a.name] = a.Id;
        }
        return attributeMap;
    }

    private static bool IsValidPriceFieldBinding(
        AdapterField field,
        MappingGraph graph,
        GraphNode sinkNode,
        ExternalField def,
        Dictionary<string, int> attributeMap)
    {
        if (field.source != null)
        {
            return CategoryImportService.IsValidPriceBinding(
                def, field.source.type, field.source.field, field.source.attributeId, attributeMap);
        }

        var inEdge = graph.edges.FirstOrDefault(e =>
            e.target == sinkNode.id && e.targetHandle == $"f:{field.fieldKey}");
        if (inEdge == null) return true;

        var srcNode = graph.nodes.FirstOrDefault(n => n.id == inEdge.source);
        return srcNode?.type switch
        {
            "productField" => CategoryImportService.IsValidPriceBinding(
                def, "productField", srcNode.data?.field, null, attributeMap),
            "attribute" => CategoryImportService.IsValidPriceBinding(
                def, "attribute", null, srcNode.data?.attributeId, attributeMap),
            "static" => def.Key.Equals("discount", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private static void ClearFieldBinding(AdapterField field, MappingGraph graph, GraphNode sinkNode)
    {
        field.source = null;
        var inbound = graph.edges
            .Where(e => e.target == sinkNode.id && e.targetHandle == $"f:{field.fieldKey}")
            .ToList();
        foreach (var edge in inbound)
        {
            var orphanId = edge.source;
            graph.edges.Remove(edge);
            if (!graph.edges.Any(e => e.source == orphanId || e.target == orphanId))
            {
                var orphan = graph.nodes.FirstOrDefault(n => n.id == orphanId);
                if (orphan != null) graph.nodes.Remove(orphan);
            }
        }
    }

    /// <summary>
    /// Converts a single-hop wire (source node → adapter field) into a direct pill binding.
    /// </summary>
    private static bool TryPromoteDirectWireToPill(AdapterField field, MappingGraph graph, GraphNode sinkNode)
    {
        if (field.source != null) return false;

        var inEdge = graph.edges.FirstOrDefault(e =>
            e.target == sinkNode.id && e.targetHandle == $"f:{field.fieldKey}");
        if (inEdge == null) return false;

        var srcNode = graph.nodes.FirstOrDefault(n => n.id == inEdge.source);
        if (srcNode?.type is not ("productField" or "attribute" or "storeAttribute" or "groupName"))
            return false;

        // Modifier chains stay on wires.
        if (graph.edges.Any(e => e.source == inEdge.source && e.target != sinkNode.id))
            return false;

        field.source = srcNode.type switch
        {
            "productField" => new AdapterFieldSource { type = "productField", field = srcNode.data?.field },
            "attribute" => new AdapterFieldSource { type = "attribute", attributeId = srcNode.data?.attributeId },
            "storeAttribute" => new AdapterFieldSource { type = "storeAttribute", attributeId = srcNode.data?.attributeId },
            "groupName" => new AdapterFieldSource { type = "groupName" },
            _ => null,
        };
        if (field.source == null) return false;

        graph.edges.Remove(inEdge);
        if (!graph.edges.Any(e => e.source == srcNode.id || e.target == srcNode.id))
            graph.nodes.Remove(srcNode);
        return true;
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
                        label = attr?.name ?? $"Attribute #{attributeId}",
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
                        label = attr?.name ?? $"Attribute #{attributeId}",
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
        Delegat.Enums.AttributeType.MultiSelect => "multiselect",
        Delegat.Enums.AttributeType.Select => "select",
        null => "any",
        _ => "string", // Text, Barcode — single string value
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

public class MappingCoverageReport
{
    public MappingCoverage Card { get; set; } = new();
    public MappingCoverage? Price { get; set; }
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
    /// <summary>"card" = adapter fields (card), "price" = price fields (price API)</summary>
    public string SinkType { get; set; } = "card";
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

public class DeterministicPriceMapResult
{
    public int Connected { get; set; }
    public int AlreadyConnected { get; set; }
    public int Skipped { get; set; }
}

public class EnsureValueMapsResult
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int AddedMappings { get; set; }
}
