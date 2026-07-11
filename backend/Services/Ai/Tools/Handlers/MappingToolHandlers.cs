using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Ai.Tools;

namespace Delegat.Services.Ai.Tools.Handlers;

public class MappingToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly AiOperationService _operationService;
    private readonly AiMappingService _mappingService;
    private readonly GraphMutationService _graphMutation;
    private readonly MappingIntelligenceService _intelligence;
    private readonly MappingCompatibilityService _compatibility;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ILogger<MappingToolHandlers> _logger;

    public MappingToolHandlers(
        DataContext context,
        AiOperationService operationService,
        AiMappingService mappingService,
        GraphMutationService graphMutation,
        MappingIntelligenceService intelligence,
        MappingCompatibilityService compatibility,
        IMarketplaceAdapterRegistry adapterRegistry,
        ILogger<MappingToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _mappingService = mappingService;
        _graphMutation = graphMutation;
        _intelligence = intelligence;
        _compatibility = compatibility;
        _adapterRegistry = adapterRegistry;
        _logger = logger;
    }

    internal async Task<(object, string)> ListMappingsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.categoryMappings.AsNoTracking()
            .Where(m => m.connection.storeId == storeId);
        if (p.ConnectionId.HasValue) q = q.Where(m => m.connectionId == p.ConnectionId);

        var mappings = await q
            .Select(m => new
            {
                m.Id, m.name, m.localCategoryId, m.externalCategoryId, m.externalCategoryName,
                m.connectionId, connectionName = m.connection.name
            })
            .ToListAsync(ct);
        return (new { mappings, count = mappings.Count }, $"{mappings.Count} маппингов");
    }

    internal async Task<(object, string)> GetMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MappingQueryInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");

        var coverageReport = await _graphMutation.GetCoverageReportAsync(p.Id, ct);
        var fields = await _graphMutation.GetFieldStatesAsync(p.Id, ct);
        var schemaContext = await _intelligence.GetSchemaContextAsync(mapping, ct);
        var productSample = await _intelligence.GetProductSampleAsync(
            mapping.localCategoryId, p.ProductId, ct);
        var adapterFields = await GetAdapterFieldDefsAsync(mapping, ct);
        var alignmentHints = await _intelligence.BuildAlignmentHintsAsync(
            mapping, adapterFields, p.ProductId, ct);
        var card = coverageReport.Card;
        var price = coverageReport.Price;
        return (new
        {
            mapping.Id, mapping.name, mapping.localCategoryId, mapping.externalCategoryId,
            mapping.externalCategoryName,
            coverage = new
            {
                card = new
                {
                    total = card.Total,
                    connected = card.Connected,
                    requiredTotal = card.RequiredTotal,
                    requiredConnected = card.RequiredConnected,
                },
                price = price == null ? null : new
                {
                    total = price.Total,
                    connected = price.Connected,
                    requiredTotal = price.RequiredTotal,
                    requiredConnected = price.RequiredConnected,
                },
            },
            fields,
            schemaContext,
            productSample,
            optionAlignmentHints = alignmentHints,
            compatibilityContextJson = mapping.compatibilityContextJson,
        }, $"Маппинг {mapping.name ?? mapping.externalCategoryName}: карточка {card.Connected}/{card.Total}" +
           (price != null ? $", цены {price.Connected}/{price.Total}" : "") +
           $" (обяз. карточка {card.RequiredConnected}/{card.RequiredTotal})");
    }

    internal async Task<(object, string)> CreateMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateMappingInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");

        var mapping = new CategoryMapping
        {
            connectionId = p.ConnectionId,
            localCategoryId = p.LocalCategoryId,
            externalCategoryId = p.ExternalCategoryId,
            externalCategoryName = p.ExternalCategoryName,
            name = p.Name
        };
        _context.categoryMappings.Add(mapping);
        await _context.SaveChangesAsync(ct);

        // PR3: No auto-generate. New compatibility flow:
        // create → get_mapping → analyze_mapping_compatibility → (questions to user) → generate_mapping_suggestions(answers)
        return (new { id = mapping.Id }, $"Маппинг создан (id={mapping.Id}). Для заполнения: get_mapping → analyze_mapping_compatibility (получить вопросы) → generate_mapping_suggestions с ответами.");
    }

    internal async Task<(object, string)> UpdateMappingNameAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateNameInput>(json);
        var mapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        mapping.name = p.Name;
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Имя маппинга обновлено: \"{p.Name}\"");
    }

    internal async Task<(object, string)> DeleteMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        _context.categoryMappings.Remove(mapping);
        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Маппинг удалён");
    }

    internal async Task<(object, string)> DuplicateMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        var copy = new CategoryMapping
        {
            connectionId = mapping.connectionId,
            localCategoryId = mapping.localCategoryId,
            externalCategoryId = mapping.externalCategoryId,
            externalCategoryName = mapping.externalCategoryName,
            name = (mapping.name ?? mapping.externalCategoryName) + " (копия)",
            graphJson = mapping.graphJson
        };
        _context.categoryMappings.Add(copy);
        await _context.SaveChangesAsync(ct);
        return (new { id = copy.Id }, $"Маппинг скопирован (id={copy.Id})");
    }

    internal async Task<(object, string)> ListExternalCategoriesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExternalCategoriesInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        var cats = await adapter.GetCategoriesAsync(conn, p.ParentId, p.Search, ct);
        return (new { categories = cats.Take(50) }, $"{cats.Count} категорий");
    }

    internal async Task<(object, string)> GetExternalFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExternalFieldsInput>(json);

        int connectionId;
        string externalCategoryId;
        if (p.MappingId.HasValue)
        {
            // Resolve connection + external category from the mapping — the AI on a mapping
            // page knows mappingId but not the marketplace category id
            var mapping = await _context.categoryMappings.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
            if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");
            connectionId = mapping.connectionId;
            externalCategoryId = mapping.externalCategoryId;
        }
        else
        {
            if (!p.ConnectionId.HasValue || string.IsNullOrEmpty(p.ExternalCategoryId))
                return (new { error = "Укажите mappingId ИЛИ connectionId+externalCategoryId" }, "Нет параметров");
            connectionId = p.ConnectionId.Value;
            externalCategoryId = p.ExternalCategoryId;
        }

        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        var fields = await adapter.GetCategoryFieldsAsync(conn, externalCategoryId, ct);

        var groupingKey = ProductSyncService.ResolveGroupingFieldKey(adapter);

        // Endpoint-based adapters group via API — the mapping's grouping field is synthetic
        // and doesn't exist in the live schema, so append it for the model
        if (groupingKey == ProductSyncService.SyntheticGroupFieldKey && fields.All(f => f.Key != groupingKey))
        {
            fields = fields.Concat([new ExternalField(
                Key: ProductSyncService.SyntheticGroupFieldKey,
                Name: "Объединение в карточку (группа)",
                IsRequired: false,
                Type: "string",
                Description: "Товары с одинаковым вычисленным значением объединяются в одну карточку. По умолчанию привязано к groupName.")]).ToList();
        }

        var projected = fields.Select(f => new
        {
            f.Key, f.Name, f.Type, f.IsRequired, f.Description,
            f.Unit, f.MaxLength, f.IsAspect,
            // Large dictionaries (WB colors/brands: thousands of values) would blow up
            // the context — show a sample and point the model at search_field_options
            options = f.Options == null ? null
                : f.Options.Take(15).Select(o => new { o.Value, o.Label }).ToList(),
            optionsTotal = f.Options?.Count,
            optionsNote = f.Options?.Count > 15
                ? "shown 15 of " + f.Options.Count + " — use search_field_options to find a specific value"
                : null,
            isGroupingField = groupingKey != null && f.Key == groupingKey ? (bool?)true : null,
        }).ToList();

        return (new
        {
            sinkType = "card",
            fields = projected,
            total = projected.Count, required = fields.Count(f => f.IsRequired)
        }, $"{projected.Count} полей карточки ({fields.Count(f => f.IsRequired)} обязательных)");
    }

    internal async Task<(object, string)> GetPriceFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExternalFieldsInput>(json);

        int connectionId;
        string externalCategoryId;
        if (p.MappingId.HasValue)
        {
            var mapping = await _context.categoryMappings.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
            if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");
            connectionId = mapping.connectionId;
            externalCategoryId = mapping.externalCategoryId;
        }
        else
        {
            if (!p.ConnectionId.HasValue || string.IsNullOrEmpty(p.ExternalCategoryId))
                return (new { error = "Укажите mappingId ИЛИ connectionId+externalCategoryId" }, "Нет параметров");
            connectionId = p.ConnectionId.Value;
            externalCategoryId = p.ExternalCategoryId;
        }

        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
            return (new { sinkType = "price", fields = Array.Empty<object>(), total = 0 },
                "Адаптер не поддерживает отдельный price API");

        var fields = await adapter.GetPriceFieldsAsync(conn, externalCategoryId, ct);
        var projected = fields.Select(f => new
        {
            f.Key, f.Name, f.Type, f.IsRequired, f.Description,
            f.Group, f.SortOrder,
            options = f.Options?.Take(15).Select(o => new { o.Value, o.Label }).ToList(),
            optionsTotal = f.Options?.Count,
        }).ToList();

        return (new
        {
            sinkType = "price",
            fields = projected,
            total = projected.Count,
            required = fields.Count(f => f.IsRequired),
        }, $"{projected.Count} полей цен ({fields.Count(f => f.IsRequired)} обязательных)");
    }

    internal async Task<(object, string)> SearchFieldOptionsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SearchFieldOptionsInput>(json);

        CategoryMapping? mapping = null;
        if (p.MappingId.HasValue)
        {
            mapping = await _context.categoryMappings.AsNoTracking()
                .Include(m => m.connection)
                .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
            if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");
        }
        else if (!p.ConnectionId.HasValue || string.IsNullOrEmpty(p.ExternalCategoryId))
        {
            return (new { error = "Укажите mappingId ИЛИ connectionId+externalCategoryId" }, "Нет параметров");
        }

        List<ExternalField> defs;
        if (mapping != null)
        {
            defs = await GetAdapterFieldDefsAsync(mapping, ct);
        }
        else
        {
            var conn = await _context.marketplaceConnections.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
            if (conn == null) return (new { error = "Подключение не найдено" }, "Не найдено");
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            defs = await adapter.GetCategoryFieldsAsync(conn, p.ExternalCategoryId!, ct);
        }

        var field = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);
        if (field == null)
            return (new { error = $"Поле '{p.AdapterFieldKey}' не найдено у адаптера" }, "Поле не найдено");
        if (field.Options is not { Count: > 0 })
            return (new { options = Array.Empty<object>(), totalOptions = 0 },
                $"У поля '{field.Name}' нет предопределённых опций — значение свободное");

        var query = p.Query ?? "";
        var matches = string.IsNullOrWhiteSpace(query)
            ? field.Options.ToList()
            : field.Options.Where(o =>
                    (o.Label?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    o.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var limit = Math.Min(p.Limit ?? 20, 50);
        return (new
        {
            options = matches.Take(limit).Select(o => new { o.Value, o.Label }).ToList(),
            totalMatches = matches.Count,
            totalOptions = field.Options.Count,
        }, $"Найдено {matches.Count} опций по «{query}» (всего у поля {field.Options.Count})");
    }

    internal async Task<(object, string)> MergeAdapterFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MergeFieldsInput>(json);
        if (p.AliasFieldKeys.Count == 0) return (new { error = "Не указаны aliasFieldKeys" }, "Нет полей");
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.MergeFieldsAsync(p.MappingId, p.PrimaryFieldKey, p.AliasFieldKeys, ct);
        }
        catch (InvalidOperationException ex)
        {
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "merge_fields",
            $"Поля [{string.Join(", ", p.AliasFieldKeys)}] объединены с '{p.PrimaryFieldKey}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true },
            $"Объединено: {string.Join(", ", p.AliasFieldKeys)} → '{p.PrimaryFieldKey}' (экспорт в оба ключа, импорт только через primary)");
    }

    internal async Task<(object, string)> UnmergeAdapterFieldsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UnmergeFieldsInput>(json);
        if (p.FieldKeys.Count == 0) return (new { error = "Не указаны fieldKeys" }, "Нет полей");
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.UnmergeFieldsAsync(p.MappingId, p.FieldKeys, ct);
        }
        catch (InvalidOperationException ex)
        {
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "unmerge_fields",
            $"Разъединены поля [{string.Join(", ", p.FieldKeys)}]",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Разъединено: {string.Join(", ", p.FieldKeys)}");
    }

    internal async Task<(object, string)> GetAvailableSourcesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var catAttrs = await _context.categoryAttributes.AsNoTracking()
            .Where(a => a.categoryId == mapping.localCategoryId)
            .Select(a => new { a.Id, a.name, a.key, a.type, a.options }).ToListAsync(ct);
        var catAttrsProjected = catAttrs.Select(a => new
        {
            a.Id,
            a.name,
            a.key,
            a.type,
            options = Delegat.Services.Marketplace.Mapping.OptionAlignmentHelper
                .ParseAttributeOptions(a.options)
                .Take(25)
                .Select(o => new { o.Value, o.Label }),
        }).ToList();
        var storeAttrs = await _context.storeAttributes.AsNoTracking()
            .Where(a => a.storeId == storeId)
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);

        return (new
        {
            productFields = new[] { "sku", "name", "price", "barcode", "description", "storageQuantity" },
            categoryAttributes = catAttrsProjected,
            storeAttributes = storeAttrs
        }, $"Источники: {catAttrs.Count} атрибутов категории, {storeAttrs.Count} атрибутов магазина");
    }

    internal async Task<(object, string)> GetMappingCoverageAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        var report = await _graphMutation.GetCoverageReportAsync(p.Id, ct);
        return (report, $"Покрытие — карточка: {report.Card.Connected}/{report.Card.Total}" +
            (report.Price != null ? $", цены: {report.Price.Connected}/{report.Price.Total}" : "") +
            $"; обяз. карточка: {report.Card.RequiredConnected}/{report.Card.RequiredTotal}");
    }

    internal async Task<(object, string)> PreviewMappingAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<PreviewMappingInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        // Preview is complex - return basic info
        return (new { mappingId = p.MappingId, productId = p.ProductId, note = "Используйте UI редактора для предпросмотра" },
            "Предпросмотр доступен в редакторе маппинга");
    }

    internal async Task<(object, string)> GetOptionAlignmentHintsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MappingQueryInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var adapterFields = await GetAdapterFieldDefsAsync(mapping, ct);
        var hints = await _intelligence.BuildAlignmentHintsAsync(mapping, adapterFields, p.ProductId, ct);
        var schemaContext = await _intelligence.GetSchemaContextAsync(mapping, ct);
        var productSample = await _intelligence.GetProductSampleAsync(mapping.localCategoryId, p.ProductId, ct);

        return (new { schemaContext, productSample, hints },
            $"{hints.Count} подсказок optionMap" +
            (schemaContext.SchemaSource != null ? $" (схема с {schemaContext.SchemaSource.ConnectionName})" : ""));
    }

    /// <summary>
    /// PR1: analyze_mapping_compatibility tool.
    /// Returns structured CompatibilityReport + list of clarifying questions (max 5).
    /// This is a read-only analysis step — no mutations.
    /// </summary>
    internal async Task<(object, string)> AnalyzeMappingCompatibilityAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MappingQueryInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");

        try
        {
            var result = await _compatibility.AnalyzeAsync(p.Id, p.ProductId, ct);

            // Summary for the assistant message (concise)
            var r = result.Report;
            var summary = $"Анализ совместимости: {r.Summary.ClearMatches} высокоточных совпадений, " +
                          $"{r.Summary.Ambiguous} неоднозначностей, {r.Summary.IntentionallyLeftEmpty} оставлено пустыми намеренно. " +
                          $"Score: {r.CompatibilityScore:P0}";

            return (new
            {
                mappingId = result.MappingId,
                report = r,
                questions = result.Questions,
                schemaContextHint = result.SchemaContextHint,
                summary = new
                {
                    clearMatches = r.Summary.ClearMatches,
                    ambiguous = r.Summary.Ambiguous,
                    requiredGaps = r.Summary.RequiredGaps,
                    intentionallyEmpty = r.Summary.IntentionallyLeftEmpty,
                    compatibilityScore = r.CompatibilityScore
                }
            }, summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalyzeMappingCompatibility failed for {MappingId}", p.Id);
            return (new { error = ex.Message }, $"Ошибка анализа совместимости: {ex.Message}");
        }
    }

    internal async Task<(object, string)> GenerateMappingSuggestionsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<MappingQueryInput>(json);
        var mapping = await _context.categoryMappings
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var localCat = await _context.categories.Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.Id == mapping.localCategoryId, ct);
        if (localCat == null) return (new { error = "Категория не найдена" }, "Не найдена");

        var adapter = _adapterRegistry.GetByKey(mapping.connection.adapterKey)!;
        var adapterFields = await adapter.GetCategoryFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);

        // Save previous graph for rollback
        var prevGraph = mapping.graphJson ?? "";
        var result = await _mappingService.GenerateSuggestionsAsync(
            mapping, localCat, adapterFields, 
            previewProductId: p.ProductId, 
            compatibilityReport: null,   // filled in PR3 when analyze was called before
            userAnswers: p.Answers,
            ct: ct);

        // Price-API fields (Ozon/WB) — deterministic pass then AI for remainder
        var priceResult = new AiMappingResult();
        DeterministicPriceMapResult? priceDeterministic = null;
        string? priceError = null;
        IReadOnlyList<ExternalField> priceFields = Array.Empty<ExternalField>();
        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
        {
            try
            {
                priceFields = await adapter.GetPriceFieldsAsync(
                    mapping.connection, mapping.externalCategoryId, ct);
                if (priceFields.Count > 0)
                {
                    priceDeterministic = await _graphMutation.ApplyDeterministicPriceMappingsAsync(
                        p.Id, priceFields, ct);
                    priceResult = await _mappingService.GenerateSuggestionsAsync(
                        mapping, localCat, priceFields, sink: "price",
                        previewProductId: p.ProductId, ct: ct);
                }
            }
            catch (Exception ex)
            {
                priceError = ex.Message;
                _logger.LogWarning(ex, "AI price-field mapping failed for mapping {Id}", p.Id);
            }
        }

        await _operationService.RecordAsync(storeId, userId, "generate_mapping",
            $"AI-маппинг для маппинга {p.Id}: подключено {result.Connected} полей",
            AiOperationService.BuildGraphRollback(p.Id, prevGraph), ct: ct);

        return (new
        {
            card = new
            {
                connected = result.Connected,
                proposed = result.Proposed,
                unconnectedRemaining = result.Skipped,
                alreadyConnectedBefore = result.AlreadyConnected,
            },
            price = priceFields.Count > 0 || priceError != null ? new
            {
                deterministic = priceDeterministic == null ? null : new
                {
                    connected = priceDeterministic.Connected,
                    alreadyConnected = priceDeterministic.AlreadyConnected,
                    skipped = priceDeterministic.Skipped,
                },
                ai = priceResult.Connected > 0 || priceResult.Proposed > 0 ? new
                {
                    connected = priceResult.Connected,
                    proposed = priceResult.Proposed,
                    unconnectedRemaining = priceResult.Skipped,
                } : null,
                error = priceError,
            } : null,
            unknownKeys = result.UnknownKeys.Concat(priceResult.UnknownKeys).Distinct().ToList(),
            noSourceFound = result.NoSource + priceResult.NoSource,
            invalidModifiersDropped = result.InvalidModifiers.Concat(priceResult.InvalidModifiers).Distinct().ToList(),
        }, $"AI подключил {result.Connected} полей карточки" +
           (priceDeterministic?.Connected > 0 ? $", цены (авто) {priceDeterministic.Connected}" : "") +
           (priceResult.Connected > 0 ? $", цены (AI) {priceResult.Connected}" : "") +
           (priceError != null ? $". Ошибка цен: {priceError}" : "") +
           $" (осталось карточка {result.Skipped}" +
           (priceResult.Skipped > 0 ? $", цены {priceResult.Skipped}" : "") + ")");
    }

    internal async Task<(object, string)> ConnectFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectFieldInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        // Field def lets GraphMutationService append missing-but-valid fields to the graph
        var fieldDef = await ResolveFieldDefAsync(mapping, p.AdapterFieldKey, p.Sink, ct);

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.ConnectFieldAsync(p.MappingId, p.AdapterFieldKey, p.SourceType, p.SourceField, p.SourceAttributeId, p.Sink, fieldDef, ct);
        await _operationService.RecordAsync(storeId, userId, "connect_field",
            $"Поле '{p.AdapterFieldKey}' подключено к {p.SourceType}:{p.SourceField ?? p.SourceAttributeId?.ToString()}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Поле '{p.AdapterFieldKey}' подключено");
    }

    internal async Task<(object, string)> ConnectFieldsBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectFieldsBulkInput>(json);
        if (p.Connections is not { Count: > 0 })
            return (new { error = "Список подключений пуст" }, "Список пуст");

        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");

        var prevGraph = mapping.graphJson ?? "";
        var applied = 0; var errors = new List<string>();

        foreach (var conn in p.Connections)
        {
            try
            {
                var fieldDef = await ResolveFieldDefAsync(mapping, conn.AdapterFieldKey, conn.Sink, ct);
                await _graphMutation.ConnectFieldAsync(p.MappingId, conn.AdapterFieldKey,
                    conn.SourceType, conn.SourceField, conn.SourceAttributeId, conn.Sink, fieldDef, ct);
                applied++;
            }
            catch (Exception ex) { errors.Add($"'{conn.AdapterFieldKey}': {ex.Message}"); }
        }

        await _operationService.RecordAsync(storeId, userId, "connect_field",
            $"Подключено {applied} полей маппинга {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);

        var summary = $"Подключено полей: {applied}";
        if (errors.Count > 0) summary += $", ошибок: {errors.Count}";
        return (new { applied, errors }, summary);
    }

    internal async Task<(object, string)> SetFieldStaticAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SetStaticInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        // Field def: enables option validation ("Стандартное значение") and graph auto-add
        var fieldDef = await ResolveFieldDefAsync(mapping, p.AdapterFieldKey, p.Sink, ct);

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.SetStaticValueAsync(p.MappingId, p.AdapterFieldKey, p.Value, p.Sink, fieldDef, ct);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. value not in the allowed options — return the option list so the model can retry
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "set_static_value",
            $"Поле '{p.AdapterFieldKey}' = '{p.Value}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Поле '{p.AdapterFieldKey}' = '{p.Value}'");
    }

    internal async Task<(object, string)> SetFieldsStaticBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SetFieldsStaticBulkInput>(json);
        if (p.Fields is not { Count: > 0 })
            return (new { error = "Список полей пуст" }, "Список пуст");

        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");

        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var prevGraph = mapping.graphJson ?? "";
        var applied = 0; var errors = new List<string>();

        foreach (var field in p.Fields)
        {
            try
            {
                var fieldDef = await ResolveFieldDefAsync(mapping, field.AdapterFieldKey, field.Sink, ct);
                await _graphMutation.SetStaticValueAsync(p.MappingId, field.AdapterFieldKey, field.Value, field.Sink, fieldDef, ct);
                applied++;
            }
            catch (Exception ex) { errors.Add($"'{field.AdapterFieldKey}': {ex.Message}"); }
        }

        await _operationService.RecordAsync(storeId, userId, "set_static_value",
            $"Установлено статических значений: {applied} для маппинга {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);

        var summary = $"Установлено значений: {applied}";
        if (errors.Count > 0) summary += $", ошибок: {errors.Count} ({string.Join("; ", errors)})";
        return (new { applied, errors }, summary);
    }

    internal async Task<(object, string)> AddFieldModifierAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AddModifierInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var fieldDef = await ResolveFieldDefAsync(mapping, p.AdapterFieldKey, p.Sink, ct);

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.AddModifierAsync(p.MappingId, p.AdapterFieldKey, p.ModifierType, p.Params, p.Sink, fieldDef, ct);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. unknown modifier type — the message lists valid types so the model can retry
            return (new { error = ex.Message }, ex.Message);
        }
        await _operationService.RecordAsync(storeId, userId, "add_modifier",
            $"Модификатор '{p.ModifierType}' добавлен к полю '{p.AdapterFieldKey}'",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Модификатор '{p.ModifierType}' добавлен");
    }

    internal async Task<(object, string)> RemoveFieldConnectionAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<FieldKeyInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.DisconnectFieldAsync(p.MappingId, p.FieldKey, p.Sink, ct);
        await _operationService.RecordAsync(storeId, userId, "disconnect_field",
            $"Поле '{p.FieldKey}' отключено в маппинге {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Поле '{p.FieldKey}' отключено");
    }

    internal async Task<(object, string)> FindProductsWithoutMappingAsync(
        string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionMappingQueryInput>(json);
        var (conn, connError) = await ResolveConnectionAsync(storeId, p, ct);
        if (conn == null) return (new { error = connError }, connError!);

        var limit = Math.Clamp(p.Limit ?? 50, 1, 200);

        var mappingCategoryIds = await _context.categoryMappings.AsNoTracking()
            .Where(m => m.connectionId == conn.Id)
            .Select(m => m.localCategoryId)
            .Distinct()
            .ToListAsync(ct);

        if (mappingCategoryIds.Count == 0)
            return (new { error = "У подключения нет маппингов категорий" },
                "Нет маппингов для подключения");

        var mappingsByCategory = await _context.categoryMappings.AsNoTracking()
            .Where(m => m.connectionId == conn.Id)
            .Select(m => new { m.Id, m.localCategoryId, name = m.name ?? m.externalCategoryName })
            .ToListAsync(ct);
        var mappingsLookup = mappingsByCategory
            .GroupBy(m => m.localCategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var baseQuery =
            from pr in _context.products.AsNoTracking()
            where pr.storeId == storeId && pr.deletedAt == null
                  && pr.categoryId.HasValue
                  && mappingCategoryIds.Contains(pr.categoryId.Value)
            join link in _context.productMarketplaceLinks.AsNoTracking()
                    .Where(l => l.connectionId == conn.Id)
                on pr.Id equals link.productId into links
            from link in links.DefaultIfEmpty()
            where link == null || link.categoryMappingId == null
            select new
            {
                productId = pr.Id,
                name = pr.name,
                sku = pr.sku,
                categoryId = pr.categoryId,
                categoryName = pr.category!.name,
                hasLink = link != null,
                selectedMappingId = link != null ? link.categoryMappingId : (int?)null,
            };

        var totalWithout = await baseQuery.CountAsync(ct);
        var products = await baseQuery
            .OrderBy(p => p.name)
            .Take(limit)
            .ToListAsync(ct);

        var projected = products.Select(pr =>
        {
            var available = pr.categoryId.HasValue && mappingsLookup.TryGetValue(pr.categoryId.Value, out var ms)
                ? ms.Select(m => new { id = m.Id, name = m.name }).ToList()
                : null;
            return new
            {
                pr.productId,
                pr.name,
                pr.sku,
                pr.categoryId,
                pr.categoryName,
                pr.hasLink,
                pr.selectedMappingId,
                availableMappings = available,
            };
        }).ToList();

        return (new
        {
            connectionId = conn.Id,
            connectionName = conn.name,
            adapterKey = conn.adapterKey,
            totalWithoutMapping = totalWithout,
            shown = projected.Count,
            products = projected,
        }, totalWithout == 0
            ? $"Все товары с маппингами категорий уже привязаны к «{conn.name}»"
            : $"{totalWithout} товаров без назначенного маппинга для «{conn.name}» (показано {projected.Count})");
    }

    internal async Task<(object, string)> GetProductMappingSummaryAsync(
        string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionMappingQueryInput>(json);
        MarketplaceConnection? conn = null;
        if (p.ConnectionId.HasValue || !string.IsNullOrWhiteSpace(p.ConnectionName) || !string.IsNullOrWhiteSpace(p.AdapterKey))
        {
            var (resolved, err) = await ResolveConnectionAsync(storeId, p, ct);
            if (resolved == null) return (new { error = err }, err!);
            conn = resolved;
        }

        var connections = conn != null
            ? new List<MarketplaceConnection> { conn }
            : await _context.marketplaceConnections.AsNoTracking()
                .Where(c => c.storeId == storeId)
                .ToListAsync(ct);

        var summaries = new List<object>();
        int? singleWithout = null;
        int? singleWith = null;
        foreach (var connection in connections)
        {
            var mappingCategoryIds = await _context.categoryMappings.AsNoTracking()
                .Where(m => m.connectionId == connection.Id)
                .Select(m => m.localCategoryId)
                .Distinct()
                .ToListAsync(ct);

            var eligibleProducts = mappingCategoryIds.Count == 0
                ? 0
                : await _context.products.AsNoTracking()
                    .CountAsync(pr => pr.storeId == storeId && pr.deletedAt == null
                        && pr.categoryId.HasValue
                        && mappingCategoryIds.Contains(pr.categoryId.Value), ct);

            var links = await _context.productMarketplaceLinks.AsNoTracking()
                .Where(l => l.connectionId == connection.Id)
                .Select(l => new { l.productId, l.categoryMappingId })
                .ToListAsync(ct);

            var assigned = links.Where(l => l.categoryMappingId != null).ToList();
            var assignedProductIds = assigned.Select(l => l.productId).ToHashSet();

            var withoutMapping = eligibleProducts == 0
                ? 0
                : await (
                    from pr in _context.products.AsNoTracking()
                    where pr.storeId == storeId && pr.deletedAt == null
                          && pr.categoryId.HasValue
                          && mappingCategoryIds.Contains(pr.categoryId.Value)
                    join link in _context.productMarketplaceLinks.AsNoTracking()
                            .Where(l => l.connectionId == connection.Id)
                        on pr.Id equals link.productId into linksJoin
                    from link in linksJoin.DefaultIfEmpty()
                    where link == null || link.categoryMappingId == null
                    select pr.Id
                ).CountAsync(ct);

            var mappingNames = await _context.categoryMappings.AsNoTracking()
                .Where(m => m.connectionId == connection.Id)
                .Select(m => new { m.Id, name = m.name ?? m.externalCategoryName })
                .ToDictionaryAsync(m => m.Id, m => m.name, ct);

            var perMapping = assigned
                .GroupBy(l => l.categoryMappingId!.Value)
                .Select(g => new
                {
                    mappingId = g.Key,
                    mappingName = mappingNames.GetValueOrDefault(g.Key, $"#{g.Key}"),
                    productCount = g.Count(),
                })
                .OrderByDescending(x => x.productCount)
                .ToList();

            if (conn != null && connection.Id == conn.Id)
            {
                singleWithout = withoutMapping;
                singleWith = assigned.Count;
            }

            summaries.Add(new
            {
                connectionId = connection.Id,
                connectionName = connection.name,
                adapterKey = connection.adapterKey,
                eligibleProducts,
                withMapping = assigned.Count,
                withoutMapping,
                linksTotal = links.Count,
                perMapping,
            });
        }

        var headline = conn != null && singleWithout.HasValue
            ? $"«{conn.name}»: {singleWithout} без маппинга, {singleWith} с маппингом"
            : $"Сводка по {connections.Count} подключениям";

        return (new { connections = summaries }, headline);
    }

    internal async Task<(object, string)> AssignProductMappingsAsync(
        string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<AssignProductMappingsInput>(json);
        if (p.MappingId <= 0)
            return (new { error = "Укажите mappingId" }, "Нет mappingId");

        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null)
            return (new { error = "Маппинг не найден" }, "Маппинг не найден");

        var query = new ConnectionMappingQueryInput
        {
            ConnectionId = p.ConnectionId ?? mapping.connectionId,
            ConnectionName = p.ConnectionName,
        };
        var (conn, connError) = await ResolveConnectionAsync(storeId, query, ct);
        if (conn == null) return (new { error = connError }, connError!);
        if (conn.Id != mapping.connectionId)
            return (new { error = "Маппинг принадлежит другому подключению" }, "Неверное подключение");

        List<int> productIds;
        if (p.ProductIds is { Count: > 0 })
        {
            productIds = p.ProductIds.Distinct().ToList();
        }
        else if (p.ApplyToAllWithoutMapping)
        {
            productIds = await (
                from pr in _context.products.AsNoTracking()
                where pr.storeId == storeId && pr.deletedAt == null
                      && pr.categoryId == mapping.localCategoryId
                join link in _context.productMarketplaceLinks.AsNoTracking()
                        .Where(l => l.connectionId == conn.Id)
                    on pr.Id equals link.productId into links
                from link in links.DefaultIfEmpty()
                where link == null || link.categoryMappingId == null
                select pr.Id
            ).ToListAsync(ct);
        }
        else
        {
            return (new { error = "Укажите productIds или applyToAllWithoutMapping=true" },
                "Нет целевых товаров");
        }

        if (productIds.Count == 0)
            return (new { updated = 0, skipped = 0 }, "Нет товаров для назначения маппинга");

        var products = await _context.products.AsNoTracking()
            .Where(pr => productIds.Contains(pr.Id) && pr.storeId == storeId && pr.deletedAt == null)
            .Select(pr => new { pr.Id, pr.categoryId })
            .ToListAsync(ct);

        var validIds = products
            .Where(pr => pr.categoryId == mapping.localCategoryId)
            .Select(pr => pr.Id)
            .ToHashSet();
        var skipped = productIds.Count - validIds.Count;

        if (validIds.Count == 0)
            return (new { error = "Ни один товар не подходит к категории маппинга", skipped },
                "Товары другой категории");

        var existingLinks = await _context.productMarketplaceLinks
            .Where(l => validIds.Contains(l.productId) && l.connectionId == conn.Id)
            .ToListAsync(ct);
        var linkByProduct = existingLinks.ToDictionary(l => l.productId);

        var updated = 0;
        foreach (var productId in validIds)
        {
            if (linkByProduct.TryGetValue(productId, out var link))
            {
                if (link.categoryMappingId == mapping.Id) continue;
                link.categoryMappingId = mapping.Id;
            }
            else
            {
                var newLink = new ProductMarketplaceLink();
                newLink.AddFill(new ProductMarketplaceLink.AddForm
                {
                    productId = productId,
                    connectionId = conn.Id,
                    externalProductId = $"pending:{productId}",
                    status = ProductMarketplaceStatus.NotSynced,
                    categoryMappingId = mapping.Id,
                });
                _context.productMarketplaceLinks.Add(newLink);
            }
            updated++;
        }

        await _context.SaveChangesAsync(ct);

        var mappingLabel = mapping.name ?? mapping.externalCategoryName ?? $"#{mapping.Id}";
        await _operationService.RecordAsync(storeId, userId, "assign_product_mappings",
            $"Маппинг «{mappingLabel}» назначен {updated} товарам ({conn.name})",
            new { mappingId = mapping.Id, connectionId = conn.Id, productIds = validIds.ToList() },
            canRollback: false, ct: ct);

        return (new
        {
            updated,
            skipped,
            mappingId = mapping.Id,
            mappingName = mappingLabel,
            connectionId = conn.Id,
            connectionName = conn.name,
        }, $"Маппинг «{mappingLabel}» назначен {updated} товарам" +
           (skipped > 0 ? $", пропущено {skipped}" : ""));
    }

    private async Task<(MarketplaceConnection? conn, string? error)> ResolveConnectionAsync(
        int storeId, ConnectionMappingQueryInput p, CancellationToken ct)
    {
        if (p.ConnectionId is > 0)
        {
            var byId = await _context.marketplaceConnections.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
            return byId == null
                ? (null, "Подключение не найдено")
                : (byId, null);
        }

        var search = (p.ConnectionName ?? p.AdapterKey ?? "").Trim();
        if (search.Length == 0)
            return (null, "Укажите connectionId или connectionName (например «Озон»)");

        var matches = await _context.marketplaceConnections.AsNoTracking()
            .Where(c => c.storeId == storeId &&
                (EF.Functions.Like(c.name, $"%{search}%") ||
                 EF.Functions.Like(c.adapterKey, $"%{search}%")))
            .Select(c => new { c.Id, c.name, c.adapterKey })
            .ToListAsync(ct);

        if (matches.Count == 0)
            return (null, $"Подключение «{search}» не найдено — вызовите list_connections");
        if (matches.Count > 1)
            return (null,
                $"Найдено {matches.Count} подключений: {string.Join(", ", matches.Select(m => $"{m.name} (#{m.Id})"))}. Уточните connectionId.");

        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstAsync(c => c.Id == matches[0].Id, ct);
        return (conn, null);
    }

    // Fetches adapter field definitions for a mapping (mapping must have connection loaded).
    // Returns empty on adapter failure — callers degrade gracefully.
    private async Task<List<ExternalField>> GetAdapterFieldDefsAsync(CategoryMapping mapping, CancellationToken ct)
    {
        try
        {
            var adapter = _adapterRegistry.GetByKey(mapping.connection.adapterKey);
            if (adapter == null) return new();
            return await adapter.GetCategoryFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);
        }
        catch { return new(); }
    }

    private async Task<List<ExternalField>> GetPriceFieldDefsAsync(CategoryMapping mapping, CancellationToken ct)
    {
        try
        {
            var adapter = _adapterRegistry.GetByKey(mapping.connection.adapterKey);
            if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
                return new();
            return await adapter.GetPriceFieldsAsync(mapping.connection, mapping.externalCategoryId, ct);
        }
        catch { return new(); }
    }

    private async Task<ExternalField?> ResolveFieldDefAsync(
        CategoryMapping mapping, string fieldKey, string? sink, CancellationToken ct)
    {
        if (string.Equals(sink, "price", StringComparison.OrdinalIgnoreCase))
        {
            var priceDefs = await GetPriceFieldDefsAsync(mapping, ct);
            return priceDefs.FirstOrDefault(d => d.Key == fieldKey);
        }
        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var cardDef = defs.FirstOrDefault(d => d.Key == fieldKey);
        if (cardDef != null) return cardDef;
        // Auto-resolve price-only keys when sink not specified
        var priceOnly = await GetPriceFieldDefsAsync(mapping, ct);
        return priceOnly.FirstOrDefault(d => d.Key == fieldKey);
    }
}

// ---- Mapping-specific DTOs ----

internal class CreateMappingInput { public int ConnectionId { get; set; } public int LocalCategoryId { get; set; } public string ExternalCategoryId { get; set; } = ""; public string? ExternalCategoryName { get; set; } public string? Name { get; set; } }
internal class UpdateNameInput { public int Id { get; set; } public string Name { get; set; } = ""; }
internal class ExternalCategoriesInput { public int ConnectionId { get; set; } public string? ParentId { get; set; } public string? Search { get; set; } }
internal class ExternalFieldsInput { public int? ConnectionId { get; set; } public string? ExternalCategoryId { get; set; } public int? MappingId { get; set; } }
internal class SearchFieldOptionsInput { public string AdapterFieldKey { get; set; } = ""; public string? Query { get; set; } public int? MappingId { get; set; } public int? ConnectionId { get; set; } public string? ExternalCategoryId { get; set; } public int? Limit { get; set; } }
internal class MergeFieldsInput { public int MappingId { get; set; } public string PrimaryFieldKey { get; set; } = ""; public List<string> AliasFieldKeys { get; set; } = []; }
internal class UnmergeFieldsInput { public int MappingId { get; set; } public List<string> FieldKeys { get; set; } = []; }
internal class MappingQueryInput 
{ 
    public int Id { get; set; } 
    public int? ProductId { get; set; } 
    // PR2: optional user answers from compatibility questions (key = adapterFieldKey)
    public Dictionary<string, string>? Answers { get; set; } 
}
internal class PreviewMappingInput { public int MappingId { get; set; } public int ProductId { get; set; } }
internal class ConnectFieldInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string SourceType { get; set; } = ""; public string? SourceField { get; set; } public int? SourceAttributeId { get; set; } public string? Sink { get; set; } }
internal class ConnectFieldsBulkInput { public int MappingId { get; set; } public List<ConnectFieldInput> Connections { get; set; } = []; }
internal class SetStaticInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string Value { get; set; } = ""; public string? Sink { get; set; } }
internal class SetFieldsStaticBulkInput { public int MappingId { get; set; } public List<SetStaticInput> Fields { get; set; } = []; }
internal class AddModifierInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string ModifierType { get; set; } = ""; public Dictionary<string, object>? Params { get; set; } public string? Sink { get; set; } }
internal class FieldKeyInput { public int MappingId { get; set; } public string FieldKey { get; set; } = ""; public string? Sink { get; set; } }
internal class ConnectionMappingQueryInput
{
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public string? AdapterKey { get; set; }
    public int? Limit { get; set; }
}
internal class AssignProductMappingsInput
{
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public int MappingId { get; set; }
    public List<int>? ProductIds { get; set; }
    public bool ApplyToAllWithoutMapping { get; set; }
}
