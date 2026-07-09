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
    private readonly IOperationLogger _operationLogger;
    private readonly ILogger<AiMappingService> _logger;

    // Fields per model call: keeps each response well under max_tokens (a single call on a
    // 65-field category truncated mid-array) and gives the user real batch-by-batch progress
    private const int BatchSize = 15;

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
        IOperationLogger operationLogger,
        ILogger<AiMappingService> logger)
    {
        _modelService = modelService;
        _apiClient = apiClient;
        _graphMutation = graphMutation;
        _context = context;
        _operationLogger = operationLogger;
        _logger = logger;
    }

    /// <summary>
    /// Fills unconnected mapping fields with AI suggestions, batch by batch (partial success:
    /// a failed batch is logged and skipped, the rest still apply). When <paramref name="operationId"/>
    /// and <paramref name="storeId"/> are set, batch progress is streamed to the operations panel;
    /// the operation lifecycle (Start/Finish) belongs to the caller.
    /// </summary>
    public async Task<AiMappingResult> GenerateSuggestionsAsync(
        CategoryMapping mapping,
        Category localCategory,
        IReadOnlyCollection<ExternalField> adapterFields,
        string? operationId = null,
        int? storeId = null,
        CancellationToken ct = default)
    {
        async Task Report(string level, string message)
        {
            if (operationId != null && storeId != null)
                await _operationLogger.LogAsync(operationId, storeId.Value, level, message);
        }

        var model = await _modelService.GetDefaultModelAsync(ct);
        if (model == null)
        {
            await Report("error", "AI-модель не настроена — добавьте её в Настройки → AI-модели");
            return new AiMappingResult { Connected = 0, Skipped = adapterFields.Count };
        }

        // Only ask the model about fields that are not connected yet — otherwise it wastes
        // its suggestion budget on fields that ApplySuggestionsAsync will skip anyway
        var existingGraph = TryParseGraph(mapping.graphJson);
        var sinkFields = existingGraph?.nodes.FirstOrDefault(n => n.type == "adapterFields")?.data?.fields;
        var alreadyConnectedCount = 0;
        if (sinkFields != null)
        {
            var connectedKeys = sinkFields
                .Where(f => f.source != null || existingGraph!.edges.Any(e => e.targetHandle == $"f:{f.fieldKey}"))
                .Select(f => f.fieldKey)
                .ToHashSet();
            var unconnected = adapterFields.Where(f => !connectedKeys.Contains(f.Key)).ToList();
            alreadyConnectedCount = adapterFields.Count - unconnected.Count;
            adapterFields = unconnected;
        }
        if (adapterFields.Count == 0)
        {
            await Report("info", "Все поля уже подключены — AI ничего не менял");
            return new AiMappingResult { Connected = 0, Skipped = 0, AlreadyConnected = alreadyConnectedCount };
        }

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

        var decrypted = _modelService.DecryptForUse(model);
        var batches = adapterFields.Chunk(BatchSize).ToList();
        await Report("info",
            $"Модель: {model.name}. Полей к подключению: {adapterFields.Count}, " +
            $"атрибутов категории: {localAttributes.Count}. Батчей: {batches.Count}");

        var result = new AiMappingResult { AlreadyConnected = alreadyConnectedCount };
        var processed = 0;
        var failedFields = 0;

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            await Report("info", $"Батч {i + 1}/{batches.Count}: {batch.Length} полей — запрос к модели…");

            try
            {
                var prompt = BuildMappingPrompt(localCategory, localAttributes, batch, exampleMappings);
                var request = new AiRequest
                {
                    model = decrypted.modelId,
                    max_tokens = Math.Max(decrypted.maxTokens, 4096),
                    system = AiPrompts.MappingSystem,
                    messages = [new AiMessage { role = "user", content = prompt }]
                };

                var response = await _apiClient.SendAsync(decrypted, request, ct);
                var textBlock = response.content.FirstOrDefault(b => b.type == "text");
                var suggestions = textBlock?.text is { } text
                    ? ParseSuggestions(text)
                    : new List<AiFieldSuggestion>();

                if (suggestions.Count == 0)
                {
                    result.Skipped += batch.Length;
                    await Report("warning", $"Батч {i + 1}/{batches.Count}: модель не дала предложений");
                }
                else
                {
                    // Apply immediately — the graph grows batch by batch, so a later failure
                    // doesn't discard earlier work
                    var apply = await _graphMutation.ApplySuggestionsAsync(mapping.Id, suggestions, batch, ct);
                    result.Connected += apply.Applied;
                    result.Skipped += batch.Length - apply.Applied;
                    result.AlreadyConnected += apply.AlreadyConnected;
                    result.Proposed += suggestions.Count;
                    result.NoSource += apply.NoSource;
                    result.UnknownKeys.AddRange(apply.UnknownKeys);
                    result.InvalidModifiers.AddRange(apply.InvalidModifiers);

                    var note = apply.InvalidModifiers.Count > 0
                        ? $", отброшено неизвестных модификаторов: {apply.InvalidModifiers.Count}"
                        : "";
                    await Report("info",
                        $"Батч {i + 1}/{batches.Count}: подключено {apply.Applied} из {batch.Length}{note}");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedFields += batch.Length;
                result.Skipped += batch.Length;
                _logger.LogError(ex, "AI mapping batch {Batch}/{Total} failed for mapping {MappingId}",
                    i + 1, batches.Count, mapping.Id);
                await Report("warning", $"Батч {i + 1}/{batches.Count} не удался: {ex.Message} — продолжаю со следующего");
            }

            processed += batch.Length;
            if (operationId != null && storeId != null)
                await _operationLogger.ProgressAsync(operationId, storeId.Value,
                    adapterFields.Count, processed - failedFields, failedFields);
        }

        return result;
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

        // Adapter fields to map (a batch — other fields are handled in separate calls)
        sb.AppendLine("MARKETPLACE FIELDS TO MAP (suggest ONLY for these keys, other fields are handled separately):");
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

            // For select/multiselect: show allowed values only when the list is small enough.
            // Silently truncating a huge dictionary made models invent values that aren't in it.
            if (field.Options is { Count: > 0 })
            {
                if (field.Options.Count <= 30)
                {
                    var optStr = string.Join(", ", field.Options.Select(o =>
                        o.Value == o.Label ? $"\"{o.Value}\"" : $"\"{o.Value}\"({o.Label})"));
                    sb.Append($" allowedValues:[{optStr}]");
                }
                else
                {
                    sb.Append($" allowedValues: {field.Options.Count} options — TOO MANY TO LIST. " +
                              "Do NOT invent a static value for this field: connect a matching local attribute " +
                              "(values are resolved server-side) or leave it unconnected");
                }
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

    private List<AiFieldSuggestion> ParseSuggestions(string text)
    {
        // Extract JSON array from response (handles cases where model adds text)
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end < start)
        {
            _logger.LogWarning("AI mapping response contains no JSON array. Head: {Head}",
                text.Length > 300 ? text[..300] : text);
            return [];
        }

        var json = text[start..(end + 1)];

        // Parse element-by-element: one malformed suggestion (e.g. "staticValue": false where
        // a string is expected) must not discard the other fifty valid ones
        var result = new List<AiFieldSuggestion>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("AI mapping response root is not an array. Head: {Head}",
                    json.Length > 300 ? json[..300] : json);
                return [];
            }

            var dropped = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var r = el.Deserialize<RawSuggestion>(_opts);
                    if (r == null || r.Confidence < 0.4 || r.SourceType == null) continue;
                    result.Add(new AiFieldSuggestion
                    {
                        AdapterFieldKey = r.AdapterFieldKey ?? "",
                        SourceType = r.SourceType,
                        SourceField = r.SourceField,
                        SourceAttributeId = r.SourceAttributeId,
                        StaticValue = StaticValueToString(r.StaticValue),
                        Modifiers = r.Modifiers?.Select(m => new AiModifierSpec
                        {
                            Type = m.Type ?? "",
                            Params = m.Params
                        }).ToList(),
                        Confidence = r.Confidence,
                        Reason = r.Reason
                    });
                }
                catch (JsonException ex)
                {
                    dropped++;
                    _logger.LogWarning("Dropped malformed AI mapping suggestion: {Error}. Element: {El}",
                        ex.Message, el.GetRawText().Length > 200 ? el.GetRawText()[..200] : el.GetRawText());
                }
            }
            if (dropped > 0)
                _logger.LogWarning("AI mapping: dropped {Dropped} malformed suggestions, kept {Kept}", dropped, result.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogError("AI mapping response is not valid JSON: {Error}. Head: {Head}",
                ex.Message, json.Length > 300 ? json[..300] : json);
            return [];
        }
        return result;
    }

    // Models emit booleans/numbers for staticValue ("staticValue": false) — accept any scalar
    private static string? StaticValueToString(JsonElement? el) => el switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } e => e.GetString(),
        { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        { } e => e.GetRawText(), // numbers, true/false — raw literal ("650", "false")
    };

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
        [JsonPropertyName("staticValue")] public JsonElement? StaticValue { get; set; }
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
    public int AlreadyConnected { get; set; }
    public int Proposed { get; set; }
    public int NoSource { get; set; }
    public List<string> UnknownKeys { get; set; } = new();
    public List<string> InvalidModifiers { get; set; } = new();
}
