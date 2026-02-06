using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Services.Marketplace;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class CategoryMappingController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly CategoryImportService _categoryImportService;
    private readonly ILogger<CategoryMappingController> _logger;

    public CategoryMappingController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IMarketplaceAdapterRegistry adapterRegistry,
        CategoryImportService categoryImportService,
        ILogger<CategoryMappingController> logger)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _adapterRegistry = adapterRegistry;
        _categoryImportService = categoryImportService;
        _logger = logger;
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

        var mappings = await repo.FindAllAsync(
            m => m.connectionId == connectionId,
            query => query
                .Include(m => m.localCategory)
                .Include(m => m.fieldMappings));

        var result = mappings.Select(m => new
        {
            id = m.Id,
            localCategoryId = m.localCategoryId,
            localCategoryName = m.localCategory?.name,
            externalCategoryId = m.externalCategoryId,
            externalCategoryName = m.externalCategoryName,
            fieldMappingsCount = m.fieldMappings?.Count ?? 0
        }).ToList();

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

        // Проверяем что маппинг не существует
        var mappingRepo = _unitOfWork.Repository<CategoryMapping>();
        var existingMapping = await mappingRepo.FindAsync(
            m => m.connectionId == connectionId && m.localCategoryId == input.localCategoryId);

        if (existingMapping != null)
            throw ApiException.BadRequest($"Локальная категория \"{category.name}\" уже связана с внешней категорией \"{existingMapping.externalCategoryName ?? existingMapping.externalCategoryId}\". Удалите существующий маппинг сначала.");

        var mapping = new CategoryMapping();
        mapping.AddFill(new CategoryMapping.AddForm
        {
            connectionId = connectionId,
            localCategoryId = input.localCategoryId,
            externalCategoryId = input.externalCategoryId,
            externalCategoryName = input.externalCategoryName
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

        var result = await _categoryImportService.ImportExternalCategoryAsync(
            connection,
            input.externalCategoryId,
            input.localCategoryName,
            input.externalCategoryName,
            ct);

        if (!result.IsSuccess)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Ошибка импорта");

        return Ok(new
        {
            categoryMappingId = result.CategoryMappingId,
            localCategoryId = result.LocalCategoryId,
            attributesCreated = result.AttributesCreated,
            message = result.Message
        });
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
    }

    public struct ImportCategoryInput
    {
        public string externalCategoryId { get; set; }
        public string? externalCategoryName { get; set; }
        public string? localCategoryName { get; set; }
    }

    #endregion
}
