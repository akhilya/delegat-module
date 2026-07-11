using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Ai.Prompts;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Mapping;
using static Delegat.Services.Ai.MappingCompatibilityService;

namespace Delegat.Services.Ai;

public class AiMappingService
{
    private readonly AiModelConfigService _modelService;
    private readonly AiApiClient _apiClient;
    private readonly GraphMutationService _graphMutation;
    private readonly MappingIntelligenceService _intelligence;
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
        MappingIntelligenceService intelligence,
        DataContext context,
        IOperationLogger operationLogger,
        ILogger<AiMappingService> logger)
    {
        _modelService = modelService;
        _apiClient = apiClient;
        _graphMutation = graphMutation;
        _intelligence = intelligence;
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
    /// <summary>
    /// Generates AI mapping suggestions.
    /// When <paramref name="compatibilityReport"/> is provided, the method prefers
    /// the new semantic compatibility flow: only meaningful pairs, higher confidence threshold,
    /// and user answers for ambiguous cases.
    /// </summary>
    public async Task<AiMappingResult> GenerateSuggestionsAsync(
        CategoryMapping mapping,
        Category localCategory,
        IReadOnlyCollection<ExternalField> adapterFields,
        string? operationId = null,
        int? storeId = null,
        string? sink = null,
        int? previewProductId = null,
        CompatibilityReport? compatibilityReport = null,
        IReadOnlyDictionary<string, string>? userAnswers = null,
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
            await Report("error", "Нет подключённых или включённых AI-моделей. Включите модель в Настройки → ИИ.");
            return new AiMappingResult { Connected = 0, Skipped = adapterFields.Count };
        }

        // Only ask the model about fields that are not connected yet — otherwise it wastes
        // its suggestion budget on fields that ApplySuggestionsAsync will skip anyway
        var existingGraph = TryParseGraph(mapping.graphJson);
        var sinkNodeType = string.Equals(sink, "price", StringComparison.OrdinalIgnoreCase)
            ? "adapterPriceFields"
            : "adapterFields";
        var sinkNode = existingGraph?.nodes.FirstOrDefault(n => n.type == sinkNodeType);
        var sinkFields = sinkNode?.data?.fields;
        var alreadyConnectedCount = 0;
        if (sinkFields != null && sinkNode != null)
        {
            var connectedKeys = sinkFields
                .Where(f => f.source != null || existingGraph!.edges.Any(e =>
                    e.target == sinkNode.id && e.targetHandle == $"f:{f.fieldKey}"))
                .Select(f => f.fieldKey)
                .ToHashSet();
            var unconnected = adapterFields.Where(f => !connectedKeys.Contains(f.Key)).ToList();
            alreadyConnectedCount = adapterFields.Count - unconnected.Count;
            adapterFields = unconnected;
        }
        var result = new AiMappingResult { AlreadyConnected = alreadyConnectedCount };

        if (adapterFields.Count == 0)
        {
            await Report("info", "Все поля уже подключены — AI ничего не менял");
            return result;
        }

        var isPriceSink = string.Equals(sink, "price", StringComparison.OrdinalIgnoreCase);
        if (isPriceSink)
        {
            var priceAttrs = await _context.categoryAttributes.AsNoTracking()
                .Where(a => a.categoryId == localCategory.Id)
                .ToListAsync(ct);
            var priceAttributeMap = priceAttrs
                .GroupBy(a => a.key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            foreach (var a in priceAttrs)
            {
                if (!string.IsNullOrWhiteSpace(a.name) && !priceAttributeMap.ContainsKey(a.name))
                    priceAttributeMap[a.name] = a.Id;
            }

            var fieldsForAi = new List<ExternalField>();
            foreach (var field in adapterFields)
            {
                if (!CategoryImportService.IsStrictPriceSemanticKey(field.Key))
                {
                    fieldsForAi.Add(field);
                    continue;
                }

                var (expectedType, _, _) = CategoryImportService.DeterminePriceAutoMapping(field, priceAttributeMap);
                if (expectedType == Delegat.Enums.FieldMappingSourceType.Static
                    && !field.Key.Equals("discount", StringComparison.OrdinalIgnoreCase))
                {
                    result.Skipped++;
                    await Report("info",
                        $"Ценовое поле «{field.Name}»: нет подходящего атрибута — оставляю пустым (без угадывания)");
                    continue;
                }

                fieldsForAi.Add(field);
            }

            adapterFields = fieldsForAi;
            if (adapterFields.Count == 0)
            {
                await Report("success", $"Готово: подключено {result.Connected} полей");
                return result;
            }
        }

        // === Compatibility mode: deterministic apply first, LLM only for the rest ===
        bool usingCompatibilityMode = compatibilityReport != null;
        if (usingCompatibilityMode)
        {
            var report = compatibilityReport!;
            var deterministic = BuildDeterministicSuggestions(report, userAnswers);
            if (deterministic.Count > 0)
            {
                await Report("info", $"Применяю {deterministic.Count} связей из отчёта совместимости (без LLM)…");
                var allFieldDefs = adapterFields.ToList();
                var apply = await _graphMutation.ApplySuggestionsAsync(
                    mapping.Id, deterministic, sink, allFieldDefs, ct);
                result.Connected += apply.Applied;
                result.AlreadyConnected += apply.AlreadyConnected;
                result.Skipped += deterministic.Count - apply.Applied - apply.AlreadyConnected;

                var appliedKeys = deterministic.Select(s => s.AdapterFieldKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
                adapterFields = adapterFields.Where(f => !appliedKeys.Contains(f.Key)).ToList();

                // Re-check graph for newly connected fields
                existingGraph = TryParseGraph((await _context.categoryMappings
                    .Where(m => m.Id == mapping.Id)
                    .Select(m => m.graphJson)
                    .FirstAsync(ct)));
                sinkNode = existingGraph?.nodes.FirstOrDefault(n => n.type == sinkNodeType);
                sinkFields = sinkNode?.data?.fields;
                if (sinkFields != null && sinkNode != null)
                {
                    var connectedKeys = sinkFields
                        .Where(f => f.source != null || existingGraph!.edges.Any(e =>
                            e.target == sinkNode.id && e.targetHandle == $"f:{f.fieldKey}"))
                        .Select(f => f.fieldKey)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    adapterFields = adapterFields.Where(f => !connectedKeys.Contains(f.Key)).ToList();
                }
            }

            // Cached reports may omit obvious adapter fields (images, offer_id, annotation).
            var obviousSuggestions = new List<AiFieldSuggestion>();
            var obviousFieldDefs = new List<ExternalField>();
            foreach (var field in adapterFields)
            {
                var match = TryMatchStandardProductField(field);
                if (match == null || match.Score < 0.80) continue;
                obviousSuggestions.Add(new AiFieldSuggestion
                {
                    AdapterFieldKey = field.Key,
                    SourceType = "productField",
                    SourceField = match.SourceField,
                    Confidence = match.Score,
                    Reason = match.Reason,
                });
                obviousFieldDefs.Add(field);
            }

            if (obviousSuggestions.Count > 0)
            {
                await Report("info",
                    $"Подключаю {obviousSuggestions.Count} стандартных полей адаптера (изображения, артикул, …)…");
                var obviousApply = await _graphMutation.ApplySuggestionsAsync(
                    mapping.Id, obviousSuggestions, sink, obviousFieldDefs, ct);
                result.Connected += obviousApply.Applied;
                result.AlreadyConnected += obviousApply.AlreadyConnected;
                result.Skipped += obviousSuggestions.Count - obviousApply.Applied - obviousApply.AlreadyConnected;

                var obviousKeys = obviousSuggestions.Select(s => s.AdapterFieldKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                adapterFields = adapterFields.Where(f => !obviousKeys.Contains(f.Key)).ToList();
            }

            var keysForLlm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var amb in report.AmbiguousPairs)
            {
                if (userAnswers != null && userAnswers.ContainsKey(amb.AdapterFieldKey))
                    keysForLlm.Add(amb.AdapterFieldKey);
            }
            foreach (var gap in report.RequiredGaps)
            {
                if (userAnswers != null && userAnswers.ContainsKey(gap.AdapterFieldKey))
                    keysForLlm.Add(gap.AdapterFieldKey);
            }

            if (keysForLlm.Count > 0)
            {
                adapterFields = adapterFields.Where(f => keysForLlm.Contains(f.Key)).ToList();
                await Report("info", $"LLM: уточнение {adapterFields.Count} полей по ответам пользователя.");
            }
            else
            {
                adapterFields = Array.Empty<ExternalField>();
                await Report("info", "Все связи применены детерминированно — LLM не требуется.");
            }
        }

        if (adapterFields.Count == 0)
        {
            await Report("success", $"Готово: подключено {result.Connected} полей");
            return result;
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

        var schemaContext = await _intelligence.GetSchemaContextAsync(mapping, ct);
        var productSample = await _intelligence.GetProductSampleAsync(
            mapping.localCategoryId, previewProductId, ct);
        var alignmentHints = await _intelligence.BuildAlignmentHintsAsync(
            mapping, adapterFields.ToList(), previewProductId, ct);

        var decrypted = _modelService.DecryptForUse(model);
        var batches = adapterFields.Chunk(BatchSize).ToList();
        await Report("info",
            $"Модель: {model.name}. Полей к подключению: {adapterFields.Count}, " +
            $"атрибутов категории: {localAttributes.Count}. Батчей: {batches.Count}" +
            (schemaContext.SchemaSource != null
                ? $". Схема категории с: {schemaContext.SchemaSource.ConnectionName}"
                : "") +
            (alignmentHints.Count > 0 ? $". Подсказок optionMap: {alignmentHints.Count}" : ""));

        var processed = 0;
        var failedFields = 0;

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            await Report("info", $"Батч {i + 1}/{batches.Count}: {batch.Length} полей — запрос к модели…");

            try
            {
                var prompt = BuildMappingPrompt(
                    localCategory, localAttributes, batch, exampleMappings,
                    schemaContext, productSample, alignmentHints,
                    compatibilityReport, userAnswers);

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
                    EnrichSuggestionsWithOptionMaps(suggestions, localAttributes, batch, alignmentHints);
                    var apply = await _graphMutation.ApplySuggestionsAsync(mapping.Id, suggestions, sink, batch, ct);
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

        if (result.Connected > 0 || result.AlreadyConnected > 0)
        {
            var isPrice = string.Equals(sink, "price", StringComparison.OrdinalIgnoreCase);
            var vmResult = await _graphMutation.EnsureSelectValueMapsAsync(
                mapping.Id,
                isPrice ? null : adapterFields,
                isPrice ? adapterFields : null,
                ct);
            if (vmResult.Inserted > 0 || vmResult.Updated > 0)
            {
                await Report("info",
                    $"Словари optionMap: добавлено {vmResult.Inserted} модификаторов, " +
                    $"обновлено {vmResult.Updated}, строк {vmResult.AddedMappings}");
            }
        }

        return result;
    }

    private static void EnrichSuggestionsWithOptionMaps(
        List<AiFieldSuggestion> suggestions,
        List<CategoryAttribute> localAttributes,
        IReadOnlyCollection<ExternalField> batchFields,
        List<OptionAlignmentHintDto> alignmentHints)
    {
        foreach (var s in suggestions)
        {
            if (s.SourceType != "attribute" || s.SourceAttributeId == null) continue;

            var attr = localAttributes.FirstOrDefault(a => a.Id == s.SourceAttributeId);
            var field = batchFields.FirstOrDefault(f => f.Key == s.AdapterFieldKey);
            if (attr == null) continue;

            var localOpts = OptionAlignmentHelper.ParseAttributeOptions(attr.options);
            if (localOpts.Count == 0) continue;

            var hasTargetOptions = field?.Options is { Count: > 0 };
            var mapType = hasTargetOptions ? "optionMap" : "valueMap";
            if (!hasTargetOptions && field == null) continue;

            var hint = alignmentHints.FirstOrDefault(h =>
                h.AdapterFieldKey == s.AdapterFieldKey &&
                h.SourceAttributeId == s.SourceAttributeId);

            List<Dictionary<string, string>> mappings;
            if (hint?.ValueMapMappings.Count > 0)
                mappings = hint.ValueMapMappings;
            else if (!hasTargetOptions || !OptionAlignmentHelper.NeedsValueMap(localOpts, field!.Options!))
                continue;
            else
                mappings = OptionAlignmentHelper.BuildValueMapEntries(localOpts, field.Options!);

            if (hasTargetOptions)
            {
                mappings = OptionAlignmentHelper.NormalizeValueMapMappings(
                    mappings, localOpts, field!.Options!);
            }

            if (mappings.Count == 0) continue;

            var existing = s.Modifiers?.FirstOrDefault(m => m.Type is "valueMap" or "optionMap");
            if (existing != null)
            {
                existing.Type = mapType;
                existing.Params ??= new Dictionary<string, object>();
                var prev = ParseModifierMappings(existing.Params);
                existing.Params["mappings"] = hasTargetOptions
                    ? OptionAlignmentHelper.MergeValueMapMappings(prev, mappings)
                    : mappings;
                continue;
            }

            s.Modifiers ??= [];
            s.Modifiers.Add(new AiModifierSpec
            {
                Type = mapType,
                Params = new Dictionary<string, object> { ["mappings"] = mappings },
            });
        }
    }

    private static List<Dictionary<string, string>> ParseModifierMappings(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("mappings", out var raw) || raw is not System.Text.Json.JsonElement el
            || el.ValueKind != System.Text.Json.JsonValueKind.Array)
            return [];

        return el.EnumerateArray().Select(item =>
        {
            var from = item.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";
            var to = item.TryGetProperty("to", out var t) ? t.GetString() ?? "" : "";
            return new Dictionary<string, string> { ["from"] = from, ["to"] = to };
        }).ToList();
    }

    private static string BuildMappingPrompt(
        Category localCategory,
        List<CategoryAttribute> localAttributes,
        IReadOnlyCollection<ExternalField> adapterFields,
        List<CategoryMapping> exampleMappings,
        SchemaContextDto? schemaContext = null,
        ProductSampleDto? productSample = null,
        List<OptionAlignmentHintDto>? alignmentHints = null,
        CompatibilityReport? compatibilityReport = null,
        IReadOnlyDictionary<string, string>? userAnswers = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Map product catalog fields to marketplace API fields.");
        sb.AppendLine();

        if (schemaContext?.SchemaSource != null)
        {
            var src = schemaContext.SchemaSource;
            sb.AppendLine("=== CATEGORY SCHEMA ORIGIN (CRITICAL) ===");
            sb.AppendLine($"Local category \"{localCategory.name}\" was likely imported from " +
                            $"\"{src.ConnectionName}\" ({src.AdapterKey}).");
            sb.AppendLine("Local attribute option VALUES/keys are from that marketplace.");
            sb.AppendLine($"You are mapping to \"{schemaContext.CurrentConnectionName}\" ({schemaContext.CurrentAdapterKey}).");
            sb.AppendLine("For EVERY Select/MultiSelect field fed by a local attribute: option KEYS differ — " +
                            "connect attribute:{id} AND add optionMap covering ALL local category options.");
            sb.AppendLine("Match by human LABEL (e.g. Серый→Серый), NOT by assuming equal keys. " +
                            "Use optionMap (Select→Select) when target has allowedValues; valueMap only for manual valueId. " +
                            "Dictionary must work for ANY product in the category, not just preview product.");
            sb.AppendLine();
        }

        if (productSample != null)
        {
            sb.AppendLine($"=== PREVIEW PRODUCT (id={productSample.ProductId}, optional validation only) ===");
            sb.AppendLine($"Name: {productSample.Name}, SKU: {productSample.Sku}");
            sb.AppendLine("Use ONLY to sanity-check one product — optionMap must cover ALL category attribute options.");
            foreach (var a in productSample.Attributes.Take(20))
                sb.AppendLine($"- attribute:{a.AttributeId} \"{a.AttributeName}\" storedValue=\"{a.StoredValue}\"");
            sb.AppendLine();
        }

        if (alignmentHints is { Count: > 0 })
        {
            sb.AppendLine("=== PRECOMPUTED OPTION MAP HINTS (use these optionMap modifiers) ===");
            foreach (var h in alignmentHints)
            {
                sb.AppendLine($"- field:\"{h.AdapterFieldKey}\" ← attribute:{h.SourceAttributeId} \"{h.SourceAttributeName}\"");
                sb.AppendLine($"  optionMap mappings: {System.Text.Json.JsonSerializer.Serialize(h.ValueMapMappings)}");
                if (h.ProductMappedTargetValue != null)
                    sb.AppendLine($"  preview product: \"{h.ProductStoredValue}\" → target \"{h.ProductMappedTargetValue}\"");
            }
            sb.AppendLine();
        }

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

                var parsedOpts = OptionAlignmentHelper.ParseAttributeOptions(attr.options);
                if (parsedOpts.Count > 0)
                {
                    var shown = parsedOpts.Take(20)
                        .Select(o => o.Value == o.Label ? $"\"{o.Label}\"" : $"\"{o.Value}\"(label:\"{o.Label}\")");
                    sb.Append($", options:[{string.Join(", ", shown)}]");
                    if (parsedOpts.Count > 20) sb.Append($" +{parsedOpts.Count - 20} more");
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

        // === PR2: User semantic decisions (from compatibility questions) ===
        if (userAnswers != null && userAnswers.Count > 0)
        {
            sb.AppendLine("=== USER SEMANTIC DECISIONS (MUST RESPECT) ===");
            sb.AppendLine("The user has answered clarifying questions about data meaning. Use these decisions exactly:");
            foreach (var (fieldKey, answer) in userAnswers)
            {
                sb.AppendLine($"- For adapter field \"{fieldKey}\": {answer}");
            }
            sb.AppendLine("Do not contradict these decisions. If the answer says to skip or use a specific source, follow it.");
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
                    if (r == null || r.Confidence < 0.65 || r.SourceType == null) continue;
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

    private static List<AiFieldSuggestion> BuildDeterministicSuggestions(
        CompatibilityReport report,
        IReadOnlyDictionary<string, string>? userAnswers)
    {
        var suggestions = new List<AiFieldSuggestion>();

        foreach (var match in report.ClearMatches)
        {
            var s = ClearMatchToSuggestion(match);
            if (s != null) suggestions.Add(s);
        }

        if (userAnswers == null) return suggestions;

        foreach (var amb in report.AmbiguousPairs)
        {
            if (!userAnswers.TryGetValue(amb.AdapterFieldKey, out var answer)) continue;
            var s = AnswerToSuggestion(amb.AdapterFieldKey, answer, amb.Candidates);
            if (s != null) suggestions.Add(s);
        }

        foreach (var gap in report.RequiredGaps)
        {
            if (!userAnswers.TryGetValue(gap.AdapterFieldKey, out var answer)) continue;
            var s = AnswerToSuggestion(gap.AdapterFieldKey, answer, gap.TopCandidate != null
                ? [gap.TopCandidate]
                : []);
            if (s != null) suggestions.Add(s);
        }

        return suggestions;
    }

    private static AiFieldSuggestion? ClearMatchToSuggestion(ClearMatch match)
    {
        var src = match.BestSource;
        if (string.IsNullOrWhiteSpace(src.SourceType)) return null;

        return new AiFieldSuggestion
        {
            AdapterFieldKey = match.AdapterFieldKey,
            SourceType = NormalizeSourceType(src.SourceType),
            SourceAttributeId = src.SourceAttributeId,
            SourceField = src.SourceField,
            StaticValue = src.StaticValue,
            Confidence = match.Score,
            Reason = src.Reason,
        };
    }

    private static AiFieldSuggestion? AnswerToSuggestion(
        string adapterFieldKey,
        string answer,
        IReadOnlyList<CompatibilityCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(answer)
            || answer.Equals("manual", StringComparison.OrdinalIgnoreCase)
            || answer.Equals("__other__", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("оставить пустым", StringComparison.OrdinalIgnoreCase)
            || answer.Contains("пропуст", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (answer.StartsWith("attribute:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(answer["attribute:".Length..], out var attrId))
        {
            return new AiFieldSuggestion
            {
                AdapterFieldKey = adapterFieldKey,
                SourceType = "attribute",
                SourceAttributeId = attrId,
                Confidence = 1.0,
                Reason = "ответ пользователя",
            };
        }

        if (answer.StartsWith("productField:", StringComparison.OrdinalIgnoreCase))
        {
            return new AiFieldSuggestion
            {
                AdapterFieldKey = adapterFieldKey,
                SourceType = "productField",
                SourceField = answer["productField:".Length..],
                Confidence = 1.0,
                Reason = "ответ пользователя",
            };
        }

        var matched = candidates.FirstOrDefault(c =>
            string.Equals(c.SourceAttributeName, answer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.SourceField, answer, StringComparison.OrdinalIgnoreCase));
        if (matched != null)
            return ClearMatchToSuggestion(new ClearMatch
            {
                AdapterFieldKey = adapterFieldKey,
                BestSource = matched,
                Score = matched.Score,
            });

        return null;
    }

    private static string? NormalizeSourceType(string sourceType) => sourceType switch
    {
        "attribute" => "attribute",
        "productField" => "productField",
        "static" => "static",
        _ => sourceType,
    };
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
