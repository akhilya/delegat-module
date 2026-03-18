using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Services;
using PrintO.Services.Marketplace;

namespace PrintO.Controllers;

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
    private readonly ILogger<CategoryMappingController> _logger;

    private readonly IOperationLogger _operationLogger;

    public CategoryMappingController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        DataContext context,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        CategoryImportService categoryImportService,
        ILogger<CategoryMappingController> logger,
        IOperationLogger operationLogger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _context = context;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _categoryImportService = categoryImportService;
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
                fieldMappingsCount = m.fieldMappings.Count
            })
            .ToListAsync();

        return Ok(result);
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

        // Получаем поля категории с маркетплейса и создаём FieldMappings
        var fieldsCreated = 0;
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter != null && adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
        {
            try
            {
                var fields = await adapter.GetCategoryFieldsAsync(connection, input.externalCategoryId, ct);
                var fieldMappingRepo = _unitOfWork.Repository<FieldMapping>();

                // Загружаем атрибуты локальной категории для авто-маппинга
                var categoryAttributes = await _unitOfWork.Repository<CategoryAttribute>()
                    .FindAllAsync(a => a.categoryId == input.localCategoryId);
                var attributeMap = categoryAttributes.ToDictionary(a => a.key ?? a.Id.ToString(), a => a.Id);

                int sortOrder = 0;
                foreach (var field in fields.OrderBy(f => f.SortOrder))
                {
                    // Определяем автоматический источник данных
                    var (sourceType, sourceField, sourceAttributeId) = DetermineAutoMapping(field, attributeMap);

                    // Если поле маппится на локальное поле товара, то не делаем его обязательным
                    var isRequired = sourceType == FieldMappingSourceType.ProductField
                        ? false
                        : field.IsRequired;

                    var fieldMapping = new FieldMapping();
                    fieldMapping.AddFill(new FieldMapping.AddForm
                    {
                        categoryMappingId = mapping.Id,
                        externalFieldKey = field.Key,
                        externalFieldName = field.Name,
                        isRequired = isRequired,
                        isEnabled = true,
                        sourceType = sourceType,
                        sourceField = sourceField,
                        sourceAttributeId = sourceAttributeId,
                        externalFieldType = field.Type,
                        externalFieldOptions = field.Options != null && field.Options.Count > 0
                            ? JsonSerializer.Serialize(field.Options.Select(o => new { value = o.Value, label = o.Label }))
                            : null,
                        sortOrder = sortOrder++
                    });

                    await fieldMappingRepo.AddAsync(fieldMapping);
                    fieldsCreated++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch fields for category mapping {MappingId}", mapping.Id);
            }
        }

        return Ok(new { id = mapping.Id, fieldsCreated });
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
            query => query.Include(m => m.fieldMappings));

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

        // Copy all field mappings
        var fieldMappingRepo = _unitOfWork.Repository<FieldMapping>();
        var fieldsCreated = 0;

        foreach (var fm in original.fieldMappings)
        {
            var copy = new FieldMapping();
            copy.AddFill(new FieldMapping.AddForm
            {
                categoryMappingId = duplicate.Id,
                externalFieldKey = fm.externalFieldKey,
                externalFieldName = fm.externalFieldName,
                isRequired = fm.isRequired,
                isEnabled = fm.isEnabled,
                sourceType = fm.sourceType,
                sourceField = fm.sourceField,
                sourceAttributeId = fm.sourceAttributeId,
                staticValue = fm.staticValue,
                transformExpression = fm.transformExpression,
                modifiersJson = fm.modifiersJson,
                externalFieldType = fm.externalFieldType,
                externalFieldOptions = fm.externalFieldOptions,
                sortOrder = fm.sortOrder
            });

            await fieldMappingRepo.AddAsync(copy);
            fieldsCreated++;
        }

        return Ok(new { id = duplicate.Id, fieldsCreated });
    }

    /// <summary>
    /// Delete a category mapping
    /// </summary>
    [HttpDelete]
    [Route("/marketplace/connections/{connectionId}/mappings/{mappingId}")]
    public async Task<IActionResult> DeleteMapping(int connectionId, int mappingId)
    {
        await GetConnectionAsync(connectionId);

        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var mapping = await mappingRepo.FindAsync(
            m => m.Id == mappingId && m.connectionId == connectionId,
            query => query.Include(m => m.fieldMappings));

        if (mapping == null)
            throw ApiException.NotFound("Маппинг не найден");

        // Удаляем связанные field mappings
        var fieldMappingRepo = _unitOfWork.Repository<FieldMapping>();
        foreach (var fm in mapping.fieldMappings.ToList())
        {
            await fieldMappingRepo.DeleteAsync(fm);
        }

        await mappingRepo.DeleteAsync(mapping);

        return Ok(new { success = true });
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

        var categoryRepo = _unitOfWork.Repository<Category>();
        var categories = await categoryRepo.FindAllAsync(
            c => c.storeId == connection.storeId,
            query => query.Include(c => c.attributes));

        var result = categories.Select(c => new
        {
            id = c.Id,
            name = c.name,
            attributesCount = c.attributes?.Count ?? 0
        }).ToList();

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

    #endregion

    #region Input DTOs

    public struct CreateMappingInput
    {
        public int localCategoryId { get; set; }
        public string externalCategoryId { get; set; }
        public string? externalCategoryName { get; set; }
        public string? name { get; set; }
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

    #endregion
}
