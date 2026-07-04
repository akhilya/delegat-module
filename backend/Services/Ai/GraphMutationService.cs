using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace.Graph;

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

    // Connect a field directly (AdapterFieldSource — no intermediate nodes)
    public async Task ConnectFieldAsync(int mappingId, string adapterFieldKey,
        string sourceType, string? sourceField, int? sourceAttributeId, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");

        var field = sinkNode.data.fields.FirstOrDefault(f => f.fieldKey == adapterFieldKey);
        if (field == null)
            throw new InvalidOperationException($"Field '{adapterFieldKey}' not found in graph");

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

    // Set a static value for a field (creates static node + edge, removes direct source)
    public async Task SetStaticValueAsync(int mappingId, string adapterFieldKey, string value, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null)
            throw new InvalidOperationException("adapterFields node not found in graph");

        var field = sinkNode.data.fields.FirstOrDefault(f => f.fieldKey == adapterFieldKey);
        if (field == null)
            throw new InvalidOperationException($"Field '{adapterFieldKey}' not found in graph");

        // Clear direct source
        field.source = null;
        // Remove existing edges to this field
        graph.edges.RemoveAll(e => e.targetHandle == $"f:{adapterFieldKey}");

        // Create a static node
        var nodeId = $"ai-static-{adapterFieldKey}-{Guid.NewGuid():N[..8]}";
        graph.nodes.Add(new GraphNode
        {
            id = nodeId,
            type = "static",
            data = new GraphNodeData { staticValue = value, label = value }
        });

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
        string modifierType, Dictionary<string, object>? @params, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);

        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode == null)
            throw new InvalidOperationException("adapterFields node not found");

        var field = sinkNode.data?.fields?.FirstOrDefault(f => f.fieldKey == adapterFieldKey);
        if (field == null)
            throw new InvalidOperationException($"Field '{adapterFieldKey}' not found");

        // Find current edge going to this field
        var existingEdge = graph.edges.FirstOrDefault(e => e.targetHandle == $"f:{adapterFieldKey}");
        var modNodeId = $"ai-mod-{adapterFieldKey}-{Guid.NewGuid():N[..8]}";

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
            var srcNodeId = $"ai-src-{adapterFieldKey}-{Guid.NewGuid():N[..8]}";

            graph.nodes.Add(src.type switch
            {
                "productField" => new GraphNode
                {
                    id = srcNodeId, type = "productField",
                    data = new GraphNodeData { field = src.field }
                },
                "attribute" => new GraphNode
                {
                    id = srcNodeId, type = "attribute",
                    data = new GraphNodeData { attributeId = src.attributeId }
                },
                "storeAttribute" => new GraphNode
                {
                    id = srcNodeId, type = "storeAttribute",
                    data = new GraphNodeData { attributeId = src.attributeId }
                },
                _ => new GraphNode { id = srcNodeId, type = "static", data = new GraphNodeData { staticValue = "" } }
            });

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

    // Apply a list of AI suggestions to the graph (used by AiMappingService)
    public async Task<int> ApplySuggestionsAsync(int mappingId,
        IReadOnlyList<AiFieldSuggestion> suggestions, CancellationToken ct = default)
    {
        var mapping = await LoadMappingAsync(mappingId, ct);
        var graph = ParseGraph(mapping.graphJson);
        var sinkNode = FindAdapterFieldsNode(graph);
        if (sinkNode?.data?.fields == null) return 0;

        var applied = 0;
        foreach (var suggestion in suggestions)
        {
            var field = sinkNode.data.fields.FirstOrDefault(f => f.fieldKey == suggestion.AdapterFieldKey);
            if (field == null || IsFieldConnected(field, graph)) continue; // skip already connected

            if (suggestion.SourceType == null) continue;

            if (suggestion.SourceType == "static" && suggestion.StaticValue != null)
            {
                var nodeId = $"ai-static-{field.fieldKey}-{Guid.NewGuid():N[..8]}";
                graph.nodes.Add(new GraphNode
                {
                    id = nodeId, type = "static",
                    data = new GraphNodeData { staticValue = suggestion.StaticValue }
                });
                AddModifierChain(graph, nodeId, sinkNode.id, field.fieldKey!, suggestion.Modifiers);
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

                if (suggestion.Modifiers?.Count > 0)
                {
                    // Convert direct source to nodes for modifier chain
                    field.source = null;
                    var srcNodeId = $"ai-src-{field.fieldKey}-{Guid.NewGuid():N[..8]}";
                    graph.nodes.Add(BuildSourceNode(srcNodeId, suggestion));
                    AddModifierChain(graph, srcNodeId, sinkNode.id, field.fieldKey!, suggestion.Modifiers);
                }

                applied++;
            }
        }

        mapping.graphJson = SerializeGraph(graph);
        await _context.SaveChangesAsync(ct);
        return applied;
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
                var modId = $"ai-mod-{fieldKey}-{Guid.NewGuid():N[..8]}";
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

    private static GraphNode BuildSourceNode(string nodeId, AiFieldSuggestion s) => s.SourceType switch
    {
        "productField" => new GraphNode { id = nodeId, type = "productField", data = new GraphNodeData { field = s.SourceField } },
        "attribute" => new GraphNode { id = nodeId, type = "attribute", data = new GraphNodeData { attributeId = s.SourceAttributeId } },
        "storeAttribute" => new GraphNode { id = nodeId, type = "storeAttribute", data = new GraphNodeData { attributeId = s.SourceAttributeId } },
        _ => new GraphNode { id = nodeId, type = "static", data = new GraphNodeData { staticValue = "" } }
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
