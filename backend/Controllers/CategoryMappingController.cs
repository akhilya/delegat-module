using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Plugins;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class CategoryMappingController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DataContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly CategoryImportService _categoryImportService;
    private readonly PluginVersionSyncService _pluginVersionSyncService;
    private readonly ILogger<CategoryMappingController> _logger;

    private readonly IOperationLogger _operationLogger;

    public CategoryMappingController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        DataContext context,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        CategoryImportService categoryImportService,
        PluginVersionSyncService pluginVersionSyncService,
        ILogger<CategoryMappingController> logger,
        IOperationLogger operationLogger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _context = context;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _categoryImportService = categoryImportService;
        _pluginVersionSyncService = pluginVersionSyncService;
        _logger = logger;
        _operationLogger = operationLogger;
    }

    /// <summary>
    /// Get categories from marketplace
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/external-categories")]
    public async Task<IActionResult> GetExternalCategories(
        int connectionId,
        string? parentId = null,
        string? search = null,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategories))
            throw ApiException.BadRequest("Адаптер не поддерживает получение категорий");

        try
        {
            var categories = await adapter.GetCategoriesAsync(connection, parentId, search, ct);
            return Ok(categories.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                parentId = c.ParentId,
                hasChildren = c.HasChildren,
                fullPath = c.FullPath
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting categories from marketplace for connection {ConnectionId}", connectionId);
            throw ApiException.BadRequest($"Не удалось получить категории: {ex.Message}");
        }
    }

    /// <summary>
    /// Get list of category mappings for a connection
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/mappings")]
    public async Task<IActionResult> GetMappings(int connectionId)
    {
        var connection = await GetConnectionAsync(connectionId);

        var repo = _unitOfWork.Repository<CategoryMapping>();

        var result = await repo.Query()
            .Where(m => m.connectionId == connectionId)
            .Select(m => new
            {
                id = m.Id,
                name = m.name,
                localCategoryId = m.localCategoryId,
                localCategoryName = m.localCategory.name,
                externalCategoryId = m.externalCategoryId,
                externalCategoryName = m.externalCategoryName,
                graphJson = m.graphJson,
                linkedProductsCount = _context.productMarketplaceLinks.Count(l => l.categoryMappingId == m.Id)
            })
            .ToListAsync();

        // Field count now comes from the graph's adapter schema (parsed in memory, not in SQL).
        var shaped = result.Select(m => new
        {
            m.id,
            m.name,
            m.localCategoryId,
            m.localCategoryName,
            m.externalCategoryId,
            m.externalCategoryName,
            fieldMappingsCount = MappingService.SchemaFields(m.graphJson).Count,
            m.linkedProductsCount,
        });

        return Ok(shaped);
    }

    /// <summary>
    /// Create a category mapping
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings")]
    public async Task<IActionResult> CreateMapping(
        int connectionId,
        [FromBody] CreateMappingInput input,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        // Проверяем что локальная категория существует
        var categoryRepo = _unitOfWork.Repository<Category>();
        var category = await categoryRepo.FindAsync(
            c => c.Id == input.localCategoryId && c.storeId == connection.storeId);

        if (category == null)
            throw ApiException.NotFound("Локальная категория не найдена");

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();

        var mapping = new CategoryMapping();
        mapping.AddFill(new CategoryMapping.AddForm
        {
            connectionId = connectionId,
            localCategoryId = input.localCategoryId,
            externalCategoryId = input.externalCategoryId,
            externalCategoryName = input.externalCategoryName,
            name = input.name
        });

        await mappingRepo.AddAsync(mapping);

        var fieldsCreated = 0;
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter != null && adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
        {
            try
            {
                var fields = await adapter.GetCategoryFieldsAsync(connection, input.externalCategoryId, ct);
                fieldsCreated = await CreateFieldMappingsAsync(mapping, connection, input.localCategoryId, fields, adapter,
                    skipAutoMapping: input.useAiMapping == true, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch fields for category mapping {MappingId}", mapping.Id);
            }
        }

        return Ok(new { id = mapping.Id, fieldsCreated });
    }

    /// <summary>
    /// Create an external marketplace category from a local category and map it immediately.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/create-external-category")]
    public async Task<IActionResult> CreateExternalCategory(
        int connectionId,
        [FromBody] CreateExternalCategoryInput input,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.CreateCategory))
            throw ApiException.BadRequest("Адаптер не поддерживает создание категорий");

        var category = await _unitOfWork.Repository<Category>().FindAsync(
            c => c.Id == input.localCategoryId && c.storeId == connection.storeId,
            query => query.Include(c => c.attributes));

        if (category == null)
            throw ApiException.NotFound("Локальная категория не найдена");

        var request = new Models.Marketplace.DTOs.CreateCategoryRequest
        {
            Name = string.IsNullOrWhiteSpace(input.externalCategoryName) ? category.name : input.externalCategoryName.Trim(),
            Description = category.description,
            ParentId = input.parentExternalCategoryId,
            StoreId = input.storeId,
            LanguageId = input.languageId,
            Fields = category.attributes
                .OrderBy(a => a.sortOrder)
                .Select(a => new Models.Marketplace.DTOs.CreateCategoryField
                {
                    Key = a.key,
                    Name = a.name,
                    Type = MapAttributeType(a.type),
                    IsRequired = a.isRequired,
                    Description = a.description,
                    Group = a.group,
                    SortOrder = a.sortOrder,
                    Options = ParseAttributeOptions(a.options)
                })
                .ToList()
        };

        var result = await adapter.CreateCategoryAsync(connection, request, ct);
        if (!result.Success || string.IsNullOrWhiteSpace(result.ExternalCategoryId))
        {
            var errorMessage = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.ErrorMessage
                : result.Success
                    ? "Адаптер сообщил об успешном создании категории, но не вернул внешний ID категории"
                    : "Не удалось создать категорию в маркетплейсе";

            throw ApiException.BadRequest(
                errorMessage,
                result.Details);
        }

        var mapping = new CategoryMapping();
        mapping.AddFill(new CategoryMapping.AddForm
        {
            connectionId = connectionId,
            localCategoryId = category.Id,
            externalCategoryId = result.ExternalCategoryId,
            externalCategoryName = result.ExternalCategoryName ?? request.Name,
            name = input.mappingName
        });

        await _unitOfWork.Repository<CategoryMapping>().AddAsync(mapping);

        var fields = result.Fields;
        if (fields == null || fields.Count == 0)
            fields = await adapter.GetCategoryFieldsAsync(connection, result.ExternalCategoryId, ct);

        var fieldsCreated = await CreateFieldMappingsAsync(mapping, connection, category.Id, fields, adapter, ct: ct);

        return Ok(new
        {
            id = mapping.Id,
            externalCategoryId = result.ExternalCategoryId,
            externalCategoryName = result.ExternalCategoryName ?? request.Name,
            fieldsCreated
        });
    }

    /// <summary>
    /// Update a category mapping (rename)
    /// </summary>
    [HttpPatch]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}")]
    public async Task<IActionResult> UpdateMapping(
        int connectionId,
        int mappingId,
        [FromBody] UpdateMappingInput input)
    {
        await GetConnectionAsync(connectionId);

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var mapping = await mappingRepo.FindAsync(
            m => m.Id == mappingId && m.connectionId == connectionId);

        if (mapping == null)
            throw ApiException.NotFound("Маппинг не найден");

        mapping.UpdateFill(new CategoryMapping.UpdateForm
        {
            name = input.name
        });

        await mappingRepo.UpdateAsync(mapping);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Fetch the current adapter field schema and append any new fields to the mapping graph.
    /// Existing bindings and user configuration are preserved (additive sync only).
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/sync-fields")]
    public async Task<IActionResult> SyncMappingFields(
        int connectionId,
        int mappingId,
        CancellationToken ct)
    {
        await GetConnectionAsync(connectionId);

        var exists = await _context.categoryMappings.AnyAsync(
            m => m.Id == mappingId && m.connectionId == connectionId, ct);
        if (!exists)
            throw ApiException.NotFound("Маппинг не найден");

        try
        {
            var result = await _pluginVersionSyncService.SyncMappingFieldsAsync(
                connectionId, mappingId, ct);

            var message = result.TotalAdded > 0
                ? $"Добавлено полей: {result.CardFieldsAdded} карточки"
                  + (result.PriceFieldsAdded > 0 ? $", {result.PriceFieldsAdded} цены" : "")
                : "Схема актуальна — новых полей нет";

            return Ok(new
            {
                success = true,
                cardFieldsAdded = result.CardFieldsAdded,
                priceFieldsAdded = result.PriceFieldsAdded,
                totalAdded = result.TotalAdded,
                totalFromApi = result.TotalFromApi,
                existingCount = result.ExistingCount,
                addedFieldKeys = result.AddedFieldKeys,
                addedPriceFieldKeys = result.AddedPriceFieldKeys,
                message,
            });
        }
        catch (InvalidOperationException ex)
        {
            throw ApiException.BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Get the visual node graph for a mapping. Returns the saved graph, or null when none
    /// exists yet (the client then auto-migrates the current field mappings into a graph).
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph")]
    public async Task<IActionResult> GetMappingGraph(int connectionId, int mappingId, CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var mapping = await _unitOfWork.Repository<CategoryMapping>()
            .FindAsync(m => m.Id == mappingId && m.connectionId == connectionId);

        if (mapping == null)
            throw ApiException.NotFound("Маппинг не найден");

        object? graph = null;
        var graphJson = mapping.graphJson;
        if (!string.IsNullOrWhiteSpace(graphJson))
        {
            var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
            if (adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices) == true)
            {
                try
                {
                    var priceFields = await adapter.GetPriceFieldsAsync(connection, mapping.externalCategoryId, ct);
                    var priceSpecs = CategoryImportService.BuildPriceFieldSpecs(
                        priceFields,
                        new Dictionary<string, int>(),
                        skipAutoMapping: true);
                    graphJson = GraphGenerator.SyncPriceFields(graphJson, priceSpecs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync price fields in graph for mapping {MappingId}", mappingId);
                }
            }

            try { graph = JsonSerializer.Deserialize<JsonElement>(graphJson); }
            catch (JsonException) { graph = null; }
        }

        return Ok(new { hasGraph = graph != null, graph });
    }

    /// <summary>
    /// Save (or clear) the visual node graph for a mapping. A null/empty graph reverts the
    /// mapping to the legacy per-field export. Existing per-field modifiersJson is left intact,
    /// so reverse (import) of unchanged linear fields keeps working.
    /// </summary>
    [HttpPut]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph")]
    public async Task<IActionResult> SaveMappingGraph(
        int connectionId,
        int mappingId,
        [FromBody] SaveGraphInput input)
    {
        await GetConnectionAsync(connectionId);

        var repo = _unitOfWork.Repository<CategoryMapping>();
        var mapping = await repo.FindAsync(m => m.Id == mappingId && m.connectionId == connectionId);

        if (mapping == null)
            throw ApiException.NotFound("Маппинг не найден");

        mapping.graphJson = input.graph.HasValue
            && input.graph.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? input.graph.Value.GetRawText()
                : null;

        await repo.UpdateAsync(mapping);

        // The graph is the single source of truth — no per-field projection needed (input.fields ignored).
        return Ok(new { success = true, hasGraph = mapping.graphJson != null });
    }

    /// <summary>
    /// Preview the result of a node graph for a specific product. Uses the graph in the request
    /// body when provided (unsaved edits from the editor), otherwise the saved graph.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/graph/preview")]
    public async Task<IActionResult> PreviewMappingGraph(
        int connectionId,
        int mappingId,
        [FromBody] GraphPreviewInput input,
        CancellationToken ct = default)
    {
        await GetConnectionAsync(connectionId);

        var mapping = await _unitOfWork.Repository<CategoryMapping>()
            .FindAsync(m => m.Id == mappingId && m.connectionId == connectionId);

        if (mapping == null)
            throw ApiException.NotFound("Маппинг не найден");

        var graphJson = input.graph.HasValue
            && input.graph.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? input.graph.Value.GetRawText()
                : mapping.graphJson;

        if (string.IsNullOrWhiteSpace(graphJson))
            return Ok(new
            {
                outputs = new Dictionary<string, string?>(),
                fieldTypes = new Dictionary<string, string>(),
                nodeValues = new Dictionary<string, string?>(),
            });

        // Load product with all relations needed for source resolution (matches the field-mapping preview).
        var product = await _context.products
            .Include(p => p.category)
            .Include(p => p.attributeValues)
                .ThenInclude(av => av.attribute)
            .Include(p => p.storeAttributeValues)
                .ThenInclude(sav => sav.storeAttribute)
            .Include(p => p.files)
            .Include(p => p.images)
                .ThenInclude(i => i.file)
            .Include(p => p.groupMemberships)
                .ThenInclude(gm => gm.group)
            .FirstOrDefaultAsync(p => p.Id == input.productId, ct);

        if (product == null)
            throw ApiException.NotFound("Товар не найден");

        var mappingService = _serviceProvider.GetRequiredService<MappingService>();
        var eval = await mappingService.EvaluateGraphAsync(product, graphJson, mappingId, ct: ct);

        var outputs = eval.Outputs.ToDictionary(kv => kv.Key, kv => FormatGraphValue(kv.Value));
        var nodeValues = eval.NodeValues.ToDictionary(kv => kv.Key, kv => FormatGraphValue(kv.Value));
        var generativeStatuses = eval.GenerativeStatuses.ToDictionary(
            kv => kv.Key,
            kv => new
            {
                status = kv.Value.Status,
                output = kv.Value.Output,
                operationId = kv.Value.OperationId,
                errorMessage = kv.Value.ErrorMessage,
            });

        return Ok(new { outputs, fieldTypes = eval.FieldTypes, nodeValues, generativeStatuses });
    }

    /// <summary>
    /// Duplicate a category mapping with all its field mappings
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}/duplicate")]
    public async Task<IActionResult> DuplicateMapping(
        int connectionId,
        int mappingId,
        CancellationToken ct = default)
    {
        await GetConnectionAsync(connectionId);

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var original = await mappingRepo.FindAsync(
            m => m.Id == mappingId && m.connectionId == connectionId,
            query => query);

        if (original == null)
            throw ApiException.NotFound("Маппинг не найден");

        // Create the duplicate mapping
        var duplicate = new CategoryMapping();
        duplicate.AddFill(new CategoryMapping.AddForm
        {
            connectionId = original.connectionId,
            localCategoryId = original.localCategoryId,
            externalCategoryId = original.externalCategoryId,
            externalCategoryName = original.externalCategoryName,
            name = string.IsNullOrWhiteSpace(original.name)
                ? "Копия"
                : $"{original.name} (копия)"
        });

        await mappingRepo.AddAsync(duplicate);

        // The graph (single source of truth) carries all field config — clone it directly. Keys/attributeIds
        // are identical for the duplicate since it shares the same local category.
        duplicate.graphJson = original.graphJson;
        await mappingRepo.UpdateAsync(duplicate);
        var fieldsCreated = MappingService.SchemaFields(original.graphJson).Count;

        return Ok(new { id = duplicate.Id, fieldsCreated });
    }

    /// <summary>
    /// Delete a category mapping
    /// </summary>
    [HttpDelete]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}")]
    public async Task<IActionResult> DeleteMapping(
        int connectionId,
        int mappingId,
        [FromQuery] int? reassignToMappingId = null,
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        await GetConnectionAsync(connectionId);

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var mapping = await mappingRepo.FindAsync(
            m => m.Id == mappingId && m.connectionId == connectionId,
            query => query);

        if (mapping == null)
            throw ApiException.NotFound("Маппинг не найден");

        var linkedProducts = await _context.productMarketplaceLinks
            .Where(l => l.connectionId == connectionId && l.categoryMappingId == mappingId)
            .Include(l => l.product)
            .ToListAsync(ct);

        if (linkedProducts.Count > 0 && force)
        {
            // Forced delete: detach links — products stay linked to the marketplace
            // but lose their mapping until the user assigns a new one
            foreach (var link in linkedProducts)
                link.categoryMappingId = null;
            await _context.SaveChangesAsync(ct);
            await mappingRepo.DeleteAsync(mapping);
            return Ok(new { success = true, reassignedProducts = 0, detachedProducts = linkedProducts.Count });
        }

        if (linkedProducts.Count > 0)
        {
            if (!reassignToMappingId.HasValue)
            {
                var replacements = await _context.categoryMappings
                    .Where(m => m.connectionId == connectionId &&
                                m.localCategoryId == mapping.localCategoryId &&
                                m.Id != mappingId)
                    .Select(m => new
                    {
                        id = m.Id,
                        name = m.name,
                        externalCategoryId = m.externalCategoryId,
                        externalCategoryName = m.externalCategoryName
                    })
                    .ToListAsync(ct);

                throw new ApiException(
                    $"Маппинг используется товарами: {linkedProducts.Count}. Выберите другой маппинг для переназначения перед удалением.",
                    StatusCodes.Status409Conflict,
                    data: new
                    {
                        linkedProductsCount = linkedProducts.Count,
                        products = linkedProducts.Take(10).Select(l => new
                        {
                            id = l.productId,
                            name = l.product.name,
                            sku = l.product.sku
                        }),
                        replacementMappings = replacements
                    });
            }

            var replacement = await _context.categoryMappings
                .FirstOrDefaultAsync(m =>
                    m.Id == reassignToMappingId.Value &&
                    m.connectionId == connectionId &&
                    m.localCategoryId == mapping.localCategoryId &&
                    m.Id != mappingId,
                    ct);

            if (replacement == null)
                throw ApiException.BadRequest("Маппинг для переназначения не найден или относится к другой локальной категории");

            foreach (var link in linkedProducts)
                link.categoryMappingId = replacement.Id;

            await _context.SaveChangesAsync(ct);
        }

        await mappingRepo.DeleteAsync(mapping);

        return Ok(new { success = true, reassignedProducts = linkedProducts.Count });
    }

    /// <summary>
    /// Import a category from the marketplace (create local category + attributes + mappings)
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/import-category")]
    public async Task<IActionResult> ImportCategory(
        int connectionId,
        [FromBody] ImportCategoryInput input,
        CancellationToken ct)
    {
        var connection = await GetConnectionAsync(connectionId);

        // Validate localCategoryId if provided
        if (input.localCategoryId.HasValue)
        {
            var categoryRepo = _unitOfWork.Repository<Category>();
            var existingCategory = await categoryRepo.FindAsync(
                c => c.Id == input.localCategoryId.Value && c.storeId == connection.storeId);
            if (existingCategory == null)
                throw ApiException.NotFound("Локальная категория не найдена");
        }

        var storeId = _currentUserService.GetSelectedStoreId() ?? connection.storeId;

        if (!string.IsNullOrEmpty(input.operationId))
            await _operationLogger.LogAsync(input.operationId, storeId, "info",
                $"Импорт категории: {input.externalCategoryName ?? input.externalCategoryId}...");

        var result = await _categoryImportService.ImportExternalCategoryAsync(
            connection,
            input.externalCategoryId,
            input.localCategoryName,
            input.externalCategoryName,
            input.localCategoryId,
            input.selectedFieldKeys,
            ct);

        if (!result.IsSuccess)
        {
            if (!string.IsNullOrEmpty(input.operationId))
                await _operationLogger.LogAsync(input.operationId, storeId, "error",
                    $"Ошибка импорта категории: {result.ErrorMessage}");
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка импорта");
        }

        if (!string.IsNullOrEmpty(input.operationId))
            await _operationLogger.LogAsync(input.operationId, storeId, "success",
                $"Категория импортирована: {result.Message} (атрибутов: {result.AttributesCreated})");

        return Ok(new
        {
            categoryMappingId = result.CategoryMappingId,
            localCategoryId = result.LocalCategoryId,
            attributesCreated = result.AttributesCreated,
            message = result.Message
        });
    }

    /// <summary>
    /// Preview external category fields before import
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/external-category-fields")]
    public async Task<IActionResult> GetExternalCategoryFields(
        int connectionId,
        [FromQuery] string externalCategoryId,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Неизвестный адаптер: {connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
            throw ApiException.BadRequest("Адаптер не поддерживает получение полей категории");

        try
        {
            var fields = await adapter.GetCategoryFieldsAsync(connection, externalCategoryId, ct);
            var result = fields.OrderBy(f => f.SortOrder).Select(f => new
            {
                key = f.Key,
                name = f.Name,
                isRequired = f.IsRequired,
                type = f.Type,
                optionsCount = f.Options?.Count ?? 0,
                description = f.Description,
                group = f.Group,
                sortOrder = f.SortOrder,
                isBaseField = CategoryImportService.IsBaseOrSpecialField(f)
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting category fields for connection {ConnectionId}, category {CategoryId}",
                connectionId, externalCategoryId);
            throw ApiException.BadRequest($"Не удалось получить поля категории: {ex.Message}");
        }
    }

    /// <summary>
    /// Get local store categories for selection
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/local-categories")]
    public async Task<IActionResult> GetLocalCategories(int connectionId)
    {
        var connection = await GetConnectionAsync(connectionId);

        // Project directly in SQL — avoids loading LONGTEXT attribute options into memory just to count them
        var result = await _context.categories
            .AsNoTracking()
            .Where(c => c.storeId == connection.storeId)
            .Select(c => new
            {
                id = c.Id,
                name = c.name,
                attributesCount = c.attributes.Count()
            })
            .ToListAsync();

        return Ok(result);
    }

    /// <summary>
    /// Resolve available mappings for a list of products (pre-export check).
    /// </summary>
    [HttpGet]
    [Route("/marketplace/connections/{connectionId}/resolve-mappings")]
    public async Task<IActionResult> ResolveMappings(
        int connectionId,
        [FromQuery] List<int> productIds,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        if (productIds == null || productIds.Count == 0)
            return Ok(new { products = Array.Empty<object>() });

        // Load products with category info
        var products = await _context.products
            .Include(p => p.category)
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(ct);

        // Load existing links for these products
        var existingLinks = await _context.productMarketplaceLinks
            .Where(l => productIds.Contains(l.productId) && l.connectionId == connectionId)
            .ToListAsync(ct);
        var linkByProductId = existingLinks.ToDictionary(l => l.productId, l => l);

        // Load all category mappings for relevant categories
        var categoryIds = products
            .Where(p => p.categoryId.HasValue)
            .Select(p => p.categoryId!.Value)
            .Distinct()
            .ToList();

        var mappings = await _context.categoryMappings
            .Where(cm => cm.connectionId == connectionId && categoryIds.Contains(cm.localCategoryId))
            .ToListAsync(ct);

        var mappingsByCategoryId = mappings
            .GroupBy(cm => cm.localCategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = products.Select(product =>
        {
            var categoryId = product.categoryId;
            var availableMappings = categoryId.HasValue && mappingsByCategoryId.TryGetValue(categoryId.Value, out var catMappings)
                ? catMappings
                : new List<CategoryMapping>();

            linkByProductId.TryGetValue(product.Id, out var existingLink);
            var selectedMappingId = existingLink?.categoryMappingId;

            // needsSelection: multiple mappings exist AND no selection has been made yet
            var needsSelection = availableMappings.Count > 1 && selectedMappingId == null;

            return new
            {
                productId = product.Id,
                productName = product.name,
                categoryId = categoryId,
                categoryName = product.category?.name,
                selectedMappingId = selectedMappingId,
                availableMappings = availableMappings.Select(m => new
                {
                    id = m.Id,
                    name = m.name,
                    externalCategoryId = m.externalCategoryId,
                    externalCategoryName = m.externalCategoryName
                }),
                needsSelection = needsSelection
            };
        }).ToList();

        return Ok(new { products = result });
    }

    /// <summary>
    /// Save mapping selections for products before export.
    /// </summary>
    [HttpPost]
    [Route("/marketplace/connections/{connectionId}/save-mapping-selections")]
    public async Task<IActionResult> SaveMappingSelections(
        int connectionId,
        [FromBody] SaveMappingSelectionsInput input,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(connectionId);

        if (input.selections == null || input.selections.Count == 0)
            return Ok(new { updated = 0 });

        var productIds = input.selections.Select(s => s.productId).ToList();

        // Load existing links
        var existingLinks = await _context.productMarketplaceLinks
            .Where(l => productIds.Contains(l.productId) && l.connectionId == connectionId)
            .ToListAsync(ct);
        var linkByProductId = existingLinks.ToDictionary(l => l.productId, l => l);

        var updated = 0;
        foreach (var selection in input.selections)
        {
            if (linkByProductId.TryGetValue(selection.productId, out var link))
            {
                link.categoryMappingId = selection.categoryMappingId;
            }
            else
            {
                // Create a new link with NotSynced status.
                // Use a unique placeholder for externalProductId to satisfy the
                // (externalProductId, connectionId) unique index — will be replaced
                // with the real marketplace ID on first sync/export.
                var newLink = new ProductMarketplaceLink();
                newLink.AddFill(new ProductMarketplaceLink.AddForm
                {
                    productId = selection.productId,
                    connectionId = connectionId,
                    externalProductId = $"pending:{selection.productId}",
                    status = ProductMarketplaceStatus.NotSynced,
                    categoryMappingId = selection.categoryMappingId
                });
                _context.productMarketplaceLinks.Add(newLink);
            }
            updated++;
        }

        await _context.SaveChangesAsync(ct);

        return Ok(new { updated });
    }

    #region Helpers

    private async Task<MarketplaceConnection> GetConnectionAsync(int connectionId)
    {
        var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Магазин не выбран");

        var repo = _unitOfWork.Repository<MarketplaceConnection>();

        var connection = await repo.FindAsync(
            c => c.Id == connectionId && c.storeId == selectedStoreId.Value);

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        return connection;
    }

    private async Task<int> CreateFieldMappingsAsync(
        CategoryMapping mapping,
        MarketplaceConnection connection,
        int localCategoryId,
        IReadOnlyCollection<Models.Marketplace.DTOs.ExternalField> fields,
        IMarketplaceAdapter adapter,
        bool skipAutoMapping = false,
        CancellationToken ct = default)
    {
        var categoryAttributes = await _unitOfWork.Repository<CategoryAttribute>()
            .FindAllAsync(a => a.categoryId == localCategoryId);

        var attributeMap = categoryAttributes
            .SelectMany(a => new[]
            {
                new { Key = a.key, a.Id },
                new { Key = a.name, a.Id }
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var sortOrder = 0;
        var specs = fields.OrderBy(f => f.SortOrder).Select(field =>
        {
            var (sourceType, sourceField, sourceAttributeId) = skipAutoMapping
                ? (FieldMappingSourceType.Static, (string?)null, (int?)null)
                : DetermineAutoMapping(field, attributeMap);
            return new FieldSpec
            {
                externalFieldKey = field.Key,
                externalFieldName = field.Name,
                externalFieldType = field.Type,
                isRequired = sourceType != FieldMappingSourceType.ProductField && field.IsRequired,
                isEnabled = true,
                sortOrder = sortOrder++,
                sourceType = sourceType,
                sourceField = sourceField,
                sourceAttributeId = sourceAttributeId,
            };
        }).ToList();

        // Every mapping carries a grouping field (synthetic for endpoint-based adapters)
        GraphGenerator.EnsureGroupingField(specs, adapter);

        List<FieldSpec> priceSpecs = new();
        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
        {
            try
            {
                var priceFields = await adapter.GetPriceFieldsAsync(connection, mapping.externalCategoryId, ct);
                priceSpecs = CategoryImportService.BuildPriceFieldSpecs(priceFields, attributeMap, skipAutoMapping);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch price fields for mapping {MappingId}", mapping.Id);
            }
        }

        // The graph is the single source of truth — generate it onto the (tracked) mapping entity.
        mapping.graphJson = GraphGenerator.BuildGraphJson(specs, priceSpecs);
        await _unitOfWork.Repository<CategoryMapping>().UpdateAsync(mapping);

        return specs.Count + priceSpecs.Count;
    }

    /// <summary>
    /// Format a graph output value for preview display.
    /// </summary>
    private static string? FormatGraphValue(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;
        if (value is System.Collections.IEnumerable en && value is not string)
            return string.Join(", ", en.Cast<object?>().Select(x => x?.ToString()));
        return value.ToString();
    }

    #endregion

    #region Auto-mapping helpers

    /// <summary>
    /// Determine automatic mapping for a field
    /// </summary>
    private static (FieldMappingSourceType sourceType, string? sourceField, int? sourceAttributeId)
        DetermineAutoMapping(Models.Marketplace.DTOs.ExternalField field, Dictionary<string, int> attributeMap)
    {
        var keyLower = field.Key.ToLowerInvariant();
        var nameLower = field.Name.ToLowerInvariant();

        // Попытка сопоставить с базовыми полями товара
        if (IsSkuField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "sku", null);

        if (IsNameField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "name", null);

        if (IsDescriptionField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "description", null);

        if (IsPriceField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "price", null);

        if (IsStockField(keyLower, nameLower))
            return (FieldMappingSourceType.ProductField, "storageQuantity", null);

        // Поля объединения товаров маппим на название группы
        if (IsGroupingField(field.Key, field.Name))
            return (FieldMappingSourceType.GroupName, null, null);

        // Поля изображений маппим на images товара
        if (IsImageField(field.Key, field.Name))
            return (FieldMappingSourceType.ProductField, "images", null);

        // Попытка сопоставить с атрибутами локальной категории по ключу или имени
        foreach (var kvp in attributeMap)
        {
            if (kvp.Key.Equals(field.Key, StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals(field.Name, StringComparison.OrdinalIgnoreCase))
            {
                return (FieldMappingSourceType.CategoryAttribute, null, kvp.Value);
            }
        }

        // По умолчанию - статическое пустое значение
        return (FieldMappingSourceType.Static, null, null);
    }

    private static bool IsSkuField(string key, string name)
    {
        // Ozon attribute 9024 = Артикул
        if (key == "9024") return true;
        if (key == "model" || name == "model") return true;

        var patterns = new[] { "sku", "артикул", "article", "vendor_code", "vendorcode" };
        return patterns.Any(p => key.Contains(p) || name.Contains(p));
    }

    private static bool IsNameField(string key, string name)
    {
        // Ozon attribute 4180 = Название товара
        if (key == "4180") return true;

        // Используем Contains для name, чтобы матчилось "Название товара"
        var exactPatterns = new[] { "name", "title" };
        var containsPatterns = new[] { "название", "наименование" };

        return exactPatterns.Any(p => key == p || name == p || key.StartsWith(p + "_") || key.EndsWith("_" + p)) ||
               containsPatterns.Any(p => name.Contains(p));
    }

    private static bool IsDescriptionField(string key, string name)
    {
        // Ozon attribute 4191 = Аннотация (Description)
        if (key == "4191") return true;

        var patterns = new[] { "description", "описание", "desc", "аннотация", "annotation" };
        return patterns.Any(p => key.Contains(p) || name.Contains(p));
    }

    private static bool IsPriceField(string key, string name)
    {
        var patterns = new[] { "price", "цена", "стоимость", "cost" };
        return patterns.Any(p => key == p || name == p || key.StartsWith(p + "_"));
    }

    private static bool IsStockField(string key, string name)
    {
        var exactPatterns = new[] { "stock", "остаток", "остатки", "quantity", "count" };
        if (exactPatterns.Any(p => key == p || name == p))
            return true;

        if (key.StartsWith("stock_") || key.EndsWith("_stock"))
            return true;

        return false;
    }

    private static bool IsGroupingField(string key, string name)
    {
        // Ozon поле 9048 - "Название модели (для объединения в одну карточку)"
        if (key == "9048") return true;

        var nameLower = name.ToLowerInvariant();
        return nameLower.Contains("для объединения") ||
               nameLower.Contains("объединение в карточку") ||
               nameLower.Contains("группировка товаров");
    }

    /// <summary>
    /// Checks whether a field is an image field.
    /// Such fields should not create attributes - they are handled separately.
    /// </summary>
    private static bool IsImageField(string key, string name)
    {
        var keyLower = key.ToLowerInvariant();
        var nameLower = name.ToLowerInvariant();

        // Стандартные ключи полей изображений
        if (keyLower == "images" || keyLower == "primary_image" ||
            keyLower == "image_url" || keyLower == "image_urls" ||
            keyLower == "images360" || keyLower == "color_image")
            return true;

        // Паттерны в названии для изображений
        var patterns = new[] { "изображен", "фото", "картинк", "image", "photo", "picture" };
        return patterns.Any(p => nameLower.Contains(p));
    }

    private static string MapAttributeType(AttributeType type)
        => type switch
        {
            AttributeType.Number => "number",
            AttributeType.Boolean => "boolean",
            AttributeType.Select => "select",
            AttributeType.MultiSelect => "multiselect",
            AttributeType.Barcode => "string",
            _ => "string"
        };

    private static List<Models.Marketplace.DTOs.ExternalFieldOption>? ParseAttributeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var options = new List<Models.Marketplace.DTOs.ExternalFieldOption>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        options.Add(new Models.Marketplace.DTOs.ExternalFieldOption(value, value));
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var valueText = TryGetString(item, "value") ?? TryGetString(item, "id") ?? TryGetString(item, "key");
                var labelText = TryGetString(item, "label") ?? TryGetString(item, "name") ?? valueText;
                if (!string.IsNullOrWhiteSpace(valueText))
                    options.Add(new Models.Marketplace.DTOs.ExternalFieldOption(valueText, labelText ?? valueText));
            }

            return options.Count > 0 ? options : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    #endregion

    #region Input DTOs

    public struct CreateMappingInput
    {
        public int localCategoryId { get; set; }
        public string externalCategoryId { get; set; }
        public string? externalCategoryName { get; set; }
        public string? name { get; set; }
        public bool? useAiMapping { get; set; }
    }

    public struct CreateExternalCategoryInput
    {
        public int localCategoryId { get; set; }
        public string? externalCategoryName { get; set; }
        public string? parentExternalCategoryId { get; set; }
        public string? mappingName { get; set; }
        public int? storeId { get; set; }
        public int? languageId { get; set; }
    }

    public struct UpdateMappingInput
    {
        public string? name { get; set; }
    }

    public struct SaveMappingSelectionsInput
    {
        public List<MappingSelectionItem> selections { get; set; }
    }

    public struct MappingSelectionItem
    {
        public int productId { get; set; }
        public int categoryMappingId { get; set; }
    }

    public struct ImportCategoryInput
    {
        public string externalCategoryId { get; set; }
        public string? externalCategoryName { get; set; }
        public string? localCategoryName { get; set; }
        public int? localCategoryId { get; set; }
        public List<string>? selectedFieldKeys { get; set; }
        public string? operationId { get; set; }
    }

    /// <summary>
    /// Body for saving a visual node graph. A null graph clears it (reverts to per-field export).
    /// </summary>
    public struct SaveGraphInput
    {
        public JsonElement? graph { get; set; }

        /// <summary>
        /// Per-field projection of the graph back onto FieldMapping rows so the existing import
        /// (reverse) path keeps working. Reversible attribute-lanes carry a real source +
        /// modifiersJson; fan-in / op / non-attribute lanes are Static (import skips them).
        /// </summary>
        public List<FieldProjection>? fields { get; set; }
    }

    /// <summary>
    /// One field's projected source + modifier chain (computed by the editor from the graph).
    /// </summary>
    public struct FieldProjection
    {
        public string externalFieldKey { get; set; }
        public FieldMappingSourceType sourceType { get; set; }
        public int? sourceAttributeId { get; set; }
        public string? modifiersJson { get; set; }
    }

    /// <summary>
    /// Body for previewing a node graph against a product. graph is optional (unsaved edits).
    /// </summary>
    public struct GraphPreviewInput
    {
        public int productId { get; set; }
        public JsonElement? graph { get; set; }
    }

    #endregion
}
