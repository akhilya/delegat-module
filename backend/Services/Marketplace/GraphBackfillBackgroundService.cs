using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Models.Marketplace;
using PrintO.Services.Marketplace.Graph;

namespace PrintO.Services.Marketplace;

/// <summary>
/// TEMPORARY one-time backfill. On startup, generates a node graph (<see cref="CategoryMapping.graphJson"/>)
/// for every category mapping that doesn't have one yet, from its existing per-field rows — a C# port of the
/// frontend <c>autoMigrate</c>. Idempotent: only fills mappings whose graph is null/empty, so re-deploys are
/// no-ops. Self-contained so it's trivial to remove: delete this file + its <c>Program.cs</c> registration
/// once the backfill has run in production.
/// </summary>
public class GraphBackfillBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GraphBackfillBackgroundService> _logger;

    public GraphBackfillBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<GraphBackfillBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting before the one-off backfill.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DataContext>();

            var mappings = await context.categoryMappings
                .Include(m => m.fieldMappings)
                .Where(m => m.graphJson == null || m.graphJson == "")
                .ToListAsync(stoppingToken);

            var migrated = 0;
            foreach (var m in mappings)
            {
                if (m.fieldMappings == null || m.fieldMappings.Count == 0)
                    continue;
                try
                {
                    m.graphJson = BuildGraphJson(m);
                    migrated++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GraphBackfill] failed to build graph for mapping {Id}", m.Id);
                }
            }

            if (migrated > 0)
                await context.SaveChangesAsync(stoppingToken);

            _logger.LogInformation(
                "[GraphBackfill] migrated {Migrated} of {Total} mapping(s) without a graph",
                migrated, mappings.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GraphBackfill] backfill run failed");
        }
    }

    // ---- autoMigrate port (mirrors frontend graphModel.js) ----

    private static readonly JsonSerializerOptions SerializeOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class ModDesc
    {
        public string? type { get; set; }
        public bool? enabled { get; set; }
        public Dictionary<string, object>? @params { get; set; }
    }

    /// <summary>Build the graphJson string for a mapping from its enabled field mappings.</summary>
    public static string BuildGraphJson(CategoryMapping mapping)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();
        var fields = new List<AdapterField>();
        var seq = 0;
        string NewId(string prefix) => $"{prefix}{seq++}";

        var enabled = (mapping.fieldMappings ?? new List<FieldMapping>())
            .Where(fm => fm.isEnabled)
            .OrderBy(fm => fm.sortOrder)
            .ToList();

        foreach (var fm in enabled)
        {
            var field = new AdapterField
            {
                fieldKey = fm.externalFieldKey,
                label = fm.externalFieldName ?? fm.externalFieldKey,
                outputType = fm.externalFieldType ?? "string",
                isRequired = fm.isRequired,
            };

            var mods = ParseActiveModifiers(fm.modifiersJson);

            // Trivial 1:1 fields (no transforms, a standard source) bind directly on the adapter — no node/wire.
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

            var source = MakeSourceNode(fm, NewId);
            nodes.Add(source);
            var prevId = source.id;

            foreach (var mod in mods)
            {
                var modNode = new GraphNode
                {
                    id = NewId("mod"),
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
            edges.Add(MakeEdge(prevId, "adapter-fields", $"f:{fm.externalFieldKey}"));
        }

        nodes.Add(new GraphNode
        {
            id = "adapter-fields",
            type = "adapterFields",
            data = new GraphNodeData { fields = fields },
        });

        return JsonSerializer.Serialize(new MappingGraph { nodes = nodes, edges = edges }, SerializeOpts);
    }

    private static AdapterFieldSource? DirectBinding(FieldMapping fm) => fm.sourceType switch
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

    private static GraphNode MakeSourceNode(FieldMapping fm, Func<string, string> newId)
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
                data = new GraphNodeData { label = "Название группы", valueType = valueType },
            },
            _ => new GraphNode
            {
                id = newId("static"),
                type = "static",
                data = new GraphNodeData
                {
                    staticValue = fm.staticValue,
                    label = string.IsNullOrEmpty(fm.staticValue) ? "Статическое значение" : fm.staticValue,
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
            var list = JsonSerializer.Deserialize<List<ModDesc>>(modifiersJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
