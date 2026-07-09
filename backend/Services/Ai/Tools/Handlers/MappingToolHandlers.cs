using Microsoft.EntityFrameworkCore;
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
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ILogger<MappingToolHandlers> _logger;

    public MappingToolHandlers(
        DataContext context,
        AiOperationService operationService,
        AiMappingService mappingService,
        GraphMutationService graphMutation,
        IMarketplaceAdapterRegistry adapterRegistry,
        ILogger<MappingToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _mappingService = mappingService;
        _graphMutation = graphMutation;
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
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Маппинг не найден" }, "Не найден");

        var coverage = await _graphMutation.GetCoverageAsync(p.Id, ct);
        var fields = await _graphMutation.GetFieldStatesAsync(p.Id, ct);
        return (new
        {
            mapping.Id, mapping.name, mapping.localCategoryId, mapping.externalCategoryId,
            mapping.externalCategoryName,
            // Counters only — the full per-field state (incl. isRequired/connected/source)
            // lives in `fields`; a separate unmapped-required list would duplicate it
            coverage = new
            {
                total = coverage.Total,
                connected = coverage.Connected,
                requiredTotal = coverage.RequiredTotal,
                requiredConnected = coverage.RequiredConnected,
            },
            fields,
        }, $"Маппинг {mapping.name ?? mapping.externalCategoryName}: {coverage.Connected}/{coverage.Total} полей " +
           $"(обязательных {coverage.RequiredConnected}/{coverage.RequiredTotal})");
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

        // AI auto-mapping
        try
        {
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            var adapterFields = await adapter.GetCategoryFieldsAsync(conn, p.ExternalCategoryId, ct);
            var localCat = await _context.categories.Include(c => c.attributes)
                .FirstOrDefaultAsync(c => c.Id == p.LocalCategoryId, ct);
            if (localCat != null)
            {
                mapping = await _context.categoryMappings.FindAsync([mapping.Id], ct) ?? mapping;
                mapping.connection = conn;
                var aiResult = await _mappingService.GenerateSuggestionsAsync(mapping, localCat, adapterFields, ct: ct);
                return (new
                {
                    id = mapping.Id,
                    aiMapping = new { connected = aiResult.Connected, skipped = aiResult.Skipped }
                }, $"Маппинг создан, AI подключил {aiResult.Connected} полей");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI mapping failed for new mapping {Id}", mapping.Id);
        }

        return (new { id = mapping.Id }, $"Маппинг создан");
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
            fields = projected,
            total = projected.Count, required = fields.Count(f => f.IsRequired)
        }, $"{projected.Count} полей ({fields.Count(f => f.IsRequired)} обязательных)");
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
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);
        var storeAttrs = await _context.storeAttributes.AsNoTracking()
            .Where(a => a.storeId == storeId)
            .Select(a => new { a.Id, a.name, a.key, a.type }).ToListAsync(ct);

        return (new
        {
            productFields = new[] { "sku", "name", "price", "barcode", "description", "storageQuantity" },
            categoryAttributes = catAttrs,
            storeAttributes = storeAttrs
        }, $"Источники: {catAttrs.Count} атрибутов категории, {storeAttrs.Count} атрибутов магазина");
    }

    internal async Task<(object, string)> GetMappingCoverageAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == p.Id && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");
        var coverage = await _graphMutation.GetCoverageAsync(p.Id, ct);
        return (coverage, $"Покрытие: {coverage.Connected}/{coverage.Total} полей, обязательных: {coverage.RequiredConnected}/{coverage.RequiredTotal}");
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

    internal async Task<(object, string)> GenerateMappingSuggestionsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
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
        var result = await _mappingService.GenerateSuggestionsAsync(mapping, localCat, adapterFields, ct: ct);

        await _operationService.RecordAsync(storeId, userId, "generate_mapping",
            $"AI-маппинг для маппинга {p.Id}: подключено {result.Connected} полей",
            AiOperationService.BuildGraphRollback(p.Id, prevGraph), ct: ct);

        return (new
        {
            connected = result.Connected,
            proposed = result.Proposed,
            unconnectedRemaining = result.Skipped,
            alreadyConnectedBefore = result.AlreadyConnected,
            unknownKeys = result.UnknownKeys,
            noSourceFound = result.NoSource,
            invalidModifiersDropped = result.InvalidModifiers,
        }, $"AI подключил {result.Connected} новых полей (предложено {result.Proposed}, " +
           $"уже было подключено {result.AlreadyConnected}, осталось незаполненных {result.Skipped}" +
           (result.InvalidModifiers.Count > 0
               ? $"; отброшены несуществующие модификаторы: {string.Join(", ", result.InvalidModifiers.Distinct())})"
               : ")"));
    }

    internal async Task<(object, string)> ConnectFieldAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectFieldInput>(json);
        var mapping = await _context.categoryMappings.AsNoTracking()
            .Include(m => m.connection)
            .FirstOrDefaultAsync(m => m.Id == p.MappingId && m.connection.storeId == storeId, ct);
        if (mapping == null) return (new { error = "Не найден" }, "Не найден");

        // Field def lets GraphMutationService append missing-but-valid fields to the graph
        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var fieldDef = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);

        var prevGraph = mapping.graphJson ?? "";
        await _graphMutation.ConnectFieldAsync(p.MappingId, p.AdapterFieldKey, p.SourceType, p.SourceField, p.SourceAttributeId, fieldDef, ct);
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

        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var prevGraph = mapping.graphJson ?? "";
        var applied = 0; var errors = new List<string>();

        foreach (var conn in p.Connections)
        {
            try
            {
                var fieldDef = defs.FirstOrDefault(d => d.Key == conn.AdapterFieldKey);
                await _graphMutation.ConnectFieldAsync(p.MappingId, conn.AdapterFieldKey,
                    conn.SourceType, conn.SourceField, conn.SourceAttributeId, fieldDef, ct);
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
        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var fieldDef = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.SetStaticValueAsync(p.MappingId, p.AdapterFieldKey, p.Value, fieldDef, ct);
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
                var fieldDef = defs.FirstOrDefault(d => d.Key == field.AdapterFieldKey);
                await _graphMutation.SetStaticValueAsync(p.MappingId, field.AdapterFieldKey, field.Value, fieldDef, ct);
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

        var defs = await GetAdapterFieldDefsAsync(mapping, ct);
        var fieldDef = defs.FirstOrDefault(d => d.Key == p.AdapterFieldKey);

        var prevGraph = mapping.graphJson ?? "";
        try
        {
            await _graphMutation.AddModifierAsync(p.MappingId, p.AdapterFieldKey, p.ModifierType, p.Params, fieldDef, ct);
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
        await _graphMutation.DisconnectFieldAsync(p.MappingId, p.FieldKey, ct);
        await _operationService.RecordAsync(storeId, userId, "disconnect_field",
            $"Поле '{p.FieldKey}' отключено в маппинге {p.MappingId}",
            AiOperationService.BuildGraphRollback(p.MappingId, prevGraph), ct: ct);
        return (new { success = true }, $"Поле '{p.FieldKey}' отключено");
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
}

// ---- Mapping-specific DTOs ----

internal class CreateMappingInput { public int ConnectionId { get; set; } public int LocalCategoryId { get; set; } public string ExternalCategoryId { get; set; } = ""; public string? ExternalCategoryName { get; set; } public string? Name { get; set; } }
internal class UpdateNameInput { public int Id { get; set; } public string Name { get; set; } = ""; }
internal class ExternalCategoriesInput { public int ConnectionId { get; set; } public string? ParentId { get; set; } public string? Search { get; set; } }
internal class ExternalFieldsInput { public int? ConnectionId { get; set; } public string? ExternalCategoryId { get; set; } public int? MappingId { get; set; } }
internal class SearchFieldOptionsInput { public string AdapterFieldKey { get; set; } = ""; public string? Query { get; set; } public int? MappingId { get; set; } public int? ConnectionId { get; set; } public string? ExternalCategoryId { get; set; } public int? Limit { get; set; } }
internal class MergeFieldsInput { public int MappingId { get; set; } public string PrimaryFieldKey { get; set; } = ""; public List<string> AliasFieldKeys { get; set; } = []; }
internal class UnmergeFieldsInput { public int MappingId { get; set; } public List<string> FieldKeys { get; set; } = []; }
internal class PreviewMappingInput { public int MappingId { get; set; } public int ProductId { get; set; } }
internal class ConnectFieldInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string SourceType { get; set; } = ""; public string? SourceField { get; set; } public int? SourceAttributeId { get; set; } }
internal class ConnectFieldsBulkInput { public int MappingId { get; set; } public List<ConnectFieldInput> Connections { get; set; } = []; }
internal class SetStaticInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string Value { get; set; } = ""; }
internal class SetFieldsStaticBulkInput { public int MappingId { get; set; } public List<SetStaticInput> Fields { get; set; } = []; }
internal class AddModifierInput { public int MappingId { get; set; } public string AdapterFieldKey { get; set; } = ""; public string ModifierType { get; set; } = ""; public Dictionary<string, object>? Params { get; set; } }
internal class FieldKeyInput { public int MappingId { get; set; } public string FieldKey { get; set; } = ""; }
