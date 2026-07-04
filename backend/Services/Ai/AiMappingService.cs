using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Ai.Prompts;
using Delegat.Services.Marketplace.Graph;

namespace Delegat.Services.Ai;

public class AiMappingService
{
    private readonly AiModelConfigService _modelService;
    private readonly AiApiClient _apiClient;
    private readonly GraphMutationService _graphMutation;
    private readonly DataContext _context;
    private readonly ILogger<AiMappingService> _logger;

    private static readonly JsonSerializerOptions _opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AiMappingService(
        AiModelConfigService modelService,
        AiApiClient apiClient,
        GraphMutationService graphMutation,
        DataContext context,
        ILogger<AiMappingService> logger)
    {
        _modelService = modelService;
        _apiClient = apiClient;
        _graphMutation = graphMutation;
        _context = context;
        _logger = logger;
    }

    public async Task<AiMappingResult> GenerateSuggestionsAsync(
        CategoryMapping mapping,
        Category localCategory,
        IReadOnlyCollection<ExternalField> adapterFields,
        CancellationToken ct = default)
    {
        var model = await _modelService.GetDefaultModelAsync(ct);
        if (model == null)
            return new AiMappingResult { Connected = 0, Skipped = adapterFields.Count };

        // Load local category attributes
        var localAttributes = await _context.categoryAttributes
            .AsNoTracking()
            .Where(a => a.categoryId == localCategory.Id)
            .OrderBy(a => a.sortOrder)
            .ToListAsync(ct);

        // Get example mappings for the same adapter as context
        var exampleMappings = await _context.categoryMappings
            .AsNoTracking()
            .Include(m => m.connection)
            .Where(m => m.Id != mapping.Id
                     && m.connection.adapterKey == mapping.connection.adapterKey
                     && m.graphJson != null)
            .Take(2)
            .ToListAsync(ct);

        var prompt = BuildMappingPrompt(localCategory, localAttributes, adapterFields, exampleMappings);

        var decrypted = _modelService.DecryptForUse(model);
        var request = new AiRequest
        {
            model = decrypted.modelId,
            max_tokens = 4096,
            system = AiPrompts.MappingSystem,
            messages = [new AiMessage { role = "user", content = prompt }]
        };

        try
        {
            var response = await _apiClient.SendAsync(decrypted, request, ct);
            var textBlock = response.content.FirstOrDefault(b => b.type == "text");
            if (textBlock?.text == null) return new AiMappingResult { Skipped = adapterFields.Count };

            var suggestions = ParseSuggestions(textBlock.text);
            if (suggestions.Count == 0) return new AiMappingResult { Skipped = adapterFields.Count };

            var applied = await _graphMutation.ApplySuggestionsAsync(mapping.Id, suggestions, ct);
            return new AiMappingResult { Connected = applied, Skipped = adapterFields.Count - applied };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI mapping failed for mapping {MappingId}", mapping.Id);
            return new AiMappingResult { Skipped = adapterFields.Count };
        }
    }

    private static string BuildMappingPrompt(
        Category localCategory,
        List<CategoryAttribute> localAttributes,
        IReadOnlyCollection<ExternalField> adapterFields,
        List<CategoryMapping> exampleMappings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Map product catalog fields to marketplace API fields.");
        sb.AppendLine();

        // Standard product fields
        sb.AppendLine("STANDARD PRODUCT FIELDS (always available):");
        sb.AppendLine("- productField:sku — Article/SKU identifier");
        sb.AppendLine("- productField:name — Product name/title");
        sb.AppendLine("- productField:price — Price (number)");
        sb.AppendLine("- productField:barcode — Barcode/EAN");
        sb.AppendLine("- productField:description — Full text description");
        sb.AppendLine("- productField:storageQuantity — Stock quantity (number)");
        sb.AppendLine();

        // Local category attributes
        if (localAttributes.Count > 0)
        {
            sb.AppendLine($"LOCAL CATEGORY ATTRIBUTES (category: {localCategory.name}):");
            foreach (var attr in localAttributes)
            {
                sb.Append($"- attribute:{attr.Id} — \"{attr.name}\" (type: {attr.type}");
                if (attr.isRequired) sb.Append(", required");
                if (attr.group != null) sb.Append($", group:\"{attr.group}\"");
                if (attr.description != null) sb.Append($", hint:\"{attr.description}\"");

                // Show options for select/multiselect so AI can build valueMap
                if (!string.IsNullOrWhiteSpace(attr.options))
                {
                    try
                    {
                        var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(attr.options);
                        if (parsed is { Count: > 0 })
                        {
                            var shown = parsed.Take(20).ToList();
                            sb.Append($", options:[{string.Join(", ", shown.Select(o => $"\"{o}\""))}]");
                            if (parsed.Count > 20) sb.Append($" +{parsed.Count - 20} more");
                        }
                    }
                    catch { /* not a string list, skip */ }
                }

                sb.AppendLine(")");
            }
            sb.AppendLine();
        }

        // Adapter fields to map
        sb.AppendLine("MARKETPLACE FIELDS TO MAP:");
        foreach (var field in adapterFields)
        {
            sb.Append($"- key:\"{field.Key}\" label:\"{field.Name}\" type:{field.Type}");
            if (field.IsRequired) sb.Append(" REQUIRED");
            if (field.IsAspect) sb.Append(" ASPECT(defines variations)");
            if (field.Unit != null) sb.Append($" unit:\"{field.Unit}\"");
            if (field.MaxLength.HasValue) sb.Append($" maxLength:{field.MaxLength}");
            if (field.MinValue.HasValue) sb.Append($" minValue:{field.MinValue}");
            if (field.MaxValue.HasValue) sb.Append($" maxValue:{field.MaxValue}");
            if (field.ValidationPattern != null) sb.Append($" pattern:\"{field.ValidationPattern}\"");
            if (field.Description != null) sb.Append($" hint:\"{field.Description}\"");

            // For select/multiselect: show allowed values (limit to first 30 to keep prompt manageable)
            if (field.Options is { Count: > 0 })
            {
                var shown = field.Options.Take(30).ToList();
                var optStr = string.Join(", ", shown.Select(o =>
                    o.Value == o.Label ? $"\"{o.Value}\"" : $"\"{o.Value}\"({o.Label})"));
                sb.Append($" allowedValues:[{optStr}]");
                if (field.Options.Count > 30)
                    sb.Append($" (+{field.Options.Count - 30} more)");
            }

            sb.AppendLine();
        }
        sb.AppendLine();

        // Example mappings if available
        if (exampleMappings.Count > 0)
        {
            sb.AppendLine("EXAMPLE MAPPINGS FROM OTHER CATEGORIES (reference — same adapter, different category):");
            foreach (var example in exampleMappings)
            {
                var graph = TryParseGraph(example.graphJson);
                var sink = graph?.nodes.FirstOrDefault(n => n.type == "adapterFields");
                if (sink?.data?.fields == null) continue;

                sb.AppendLine($"  \"{example.name ?? example.externalCategoryName}\":");
                foreach (var field in sink.data.fields.Where(f => f.source != null))
                {
                    var src = field.source!;
                    var srcDesc = src.type switch
                    {
                        "productField" => $"productField:{src.field}",
                        "attribute" => $"attribute:{src.attributeId}",
                        "static" => $"static:\"{src.field}\"",
                        _ => src.type ?? "unknown"
                    };

                    sb.AppendLine($"    \"{field.fieldKey}\" ← {srcDesc}");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine(AiPrompts.MappingRules);
        sb.AppendLine();
        sb.AppendLine(AiPrompts.MappingResponseSchema);

        return sb.ToString();
    }

    private static List<AiFieldSuggestion> ParseSuggestions(string text)
    {
        // Extract JSON array from response (handles cases where model adds text)
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end < start) return [];

        var json = text[start..(end + 1)];

        try
        {
            var raw = JsonSerializer.Deserialize<List<RawSuggestion>>(json, _opts) ?? [];
            return raw
                .Where(r => r.Confidence >= 0.4 && r.SourceType != null)
                .Select(r => new AiFieldSuggestion
                {
                    AdapterFieldKey = r.AdapterFieldKey ?? "",
                    SourceType = r.SourceType,
                    SourceField = r.SourceField,
                    SourceAttributeId = r.SourceAttributeId,
                    StaticValue = r.StaticValue,
                    Modifiers = r.Modifiers?.Select(m => new AiModifierSpec
                    {
                        Type = m.Type ?? "",
                        Params = m.Params
                    }).ToList(),
                    Confidence = r.Confidence,
                    Reason = r.Reason
                })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static MappingGraph? TryParseGraph(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<MappingGraph>(json, _opts); }
        catch { return null; }
    }

    private class RawSuggestion
    {
        [JsonPropertyName("adapterFieldKey")] public string? AdapterFieldKey { get; set; }
        [JsonPropertyName("sourceType")] public string? SourceType { get; set; }
        [JsonPropertyName("sourceField")] public string? SourceField { get; set; }
        [JsonPropertyName("sourceAttributeId")] public int? SourceAttributeId { get; set; }
        [JsonPropertyName("staticValue")] public string? StaticValue { get; set; }
        [JsonPropertyName("modifiers")] public List<RawModifier>? Modifiers { get; set; }
        [JsonPropertyName("confidence")] public double Confidence { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }

    private class RawModifier
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("params")] public Dictionary<string, object>? Params { get; set; }
    }
}

public class AiMappingResult
{
    public int Connected { get; set; }
    public int Skipped { get; set; }
}
