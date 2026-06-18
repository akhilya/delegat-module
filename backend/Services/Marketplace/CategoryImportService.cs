using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Marketplace.DTOs;

namespace PrintO.Services.Marketplace;

/// <summary>
/// Service for importing categories from marketplaces.
/// </summary>
public class CategoryImportService
{
    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;

    /// <summary>
    /// Initializes a new instance of <see cref="CategoryImportService"/>.
    /// </summary>
    /// <param name="context">Database context.</param>
    /// <param name="adapterRegistry">Marketplace adapter registry.</param>
    public CategoryImportService(DataContext context, IMarketplaceAdapterRegistry adapterRegistry)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
    }

    /// <summary>
    /// Import a category from a marketplace.
    /// Creates a local category, attributes, and field mappings.
    /// </summary>
    /// <param name="connection">Marketplace connection.</param>
    /// <param name="externalCategoryId">Category ID on the marketplace.</param>
    /// <param name="localCategoryName">Local category name (falls back to marketplace name if null).</param>
    /// <param name="externalCategoryName">Category name on the marketplace (passed from frontend).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the category import operation.</returns>
    public async Task<CategoryImportResult> ImportExternalCategoryAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        string? localCategoryName = null,
        string? externalCategoryName = null,
        int? localCategoryId = null,
        List<string>? selectedFieldKeys = null,
        CancellationToken ct = default)
    {
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
        {
            return CategoryImportResult.Failure($"Adapter '{connection.adapterKey}' not found");
        }

        // Check adapter capabilities
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields))
        {
            return CategoryImportResult.Failure("Adapter does not support retrieving category fields");
        }

        // Get category fields from the marketplace
        List<ExternalField> fields;

        try
        {
            // Get category fields
            fields = await adapter.GetCategoryFieldsAsync(connection, externalCategoryId, ct);

            // Use the provided local category name or fall back to the marketplace name
            localCategoryName ??= externalCategoryName ?? $"Category {externalCategoryId}";
            // External category name comes from the provided parameter or falls back to local name
            externalCategoryName ??= localCategoryName;
        }
        catch (Exception ex)
        {
            return CategoryImportResult.Failure($"Error retrieving data from marketplace: {ex.Message}");
        }

        // Filter fields - exclude base product fields (sku, name, description, price, stock, images)
        // and grouping fields (e.g. Ozon 9048), since they don't need attribute creation
        var fieldsForAttributes = fields.Where(f => !IsBaseOrSpecialField(f)).ToList();

        // Filter by selectedFieldKeys if provided
        if (selectedFieldKeys != null && selectedFieldKeys.Count > 0)
        {
            fieldsForAttributes = fieldsForAttributes
                .Where(f => selectedFieldKeys.Contains(f.Key))
                .ToList();
        }

        // Load existing category or create new one
        Category category;
        var attributeMap = new Dictionary<string, int>(); // externalFieldKey -> categoryAttributeId
        int sortOrder = 0;
        bool isExistingCategory = false;

        if (localCategoryId.HasValue)
        {
            // Load existing category with attributes
            var existingCat = await _context.categories
                .Include(c => c.attributes)
                .FirstOrDefaultAsync(c => c.Id == localCategoryId.Value, ct);

            if (existingCat == null)
                return CategoryImportResult.Failure("Указанная локальная категория не найдена");

            category = existingCat;

            isExistingCategory = true;

            // Pre-populate attributeMap from existing attributes (sanitized key -> attribute ID)
            if (category.attributes != null)
            {
                foreach (var attr in category.attributes)
                {
                    var key = attr.key ?? attr.Id.ToString();
                    if (!attributeMap.ContainsKey(key))
                        attributeMap[key] = attr.Id;
                }
                sortOrder = category.attributes.Any()
                    ? category.attributes.Max(a => a.sortOrder) + 1
                    : 0;
            }

            localCategoryName = category.name;
        }
        else
        {
            // Создаём локальную категорию
            category = new Category();
            category.AddFill(new Category.AddForm
            {
                name = localCategoryName,
                storeId = connection.storeId
            });

            _context.categories.Add(category);
            await _context.SaveChangesAsync(ct);
        }

        // Создаём атрибуты категории только из дополнительных полей маркетплейса
        // (без базовых полей товара: sku, name, description, price, stock)
        int attributesCreated = 0;

        foreach (var field in fieldsForAttributes.OrderBy(f => f.SortOrder))
        {
            var sanitizedKey = SanitizeKey(field.Key);

            // Skip if attribute with this key already exists
            if (attributeMap.ContainsKey(sanitizedKey))
                continue;

            // Определяем значение по умолчанию для некоторых типов полей
            string? defaultValue = null;
            if (field.Options != null && field.Options.Count > 0)
            {
                defaultValue = DetermineDefaultValue(field);
            }

            var attribute = new CategoryAttribute();
            attribute.AddFill(new CategoryAttribute.AddForm
            {
                name = field.Name,
                key = sanitizedKey,
                type = MapExternalFieldType(field.Type),
                isRequired = field.IsRequired,
                sortOrder = sortOrder++,
                options = field.Options != null ? SerializeOptions(field.Options) : null,
                defaultValue = defaultValue,
                description = field.Description,
                group = field.Group ?? DetermineGroupByKeywords(field.Name),
                categoryId = category.Id
            });

            _context.categoryAttributes.Add(attribute);
            await _context.SaveChangesAsync(ct);

            attributeMap[field.Key] = attribute.Id;
            // Also index by sanitized key for consistent lookup
            if (sanitizedKey != field.Key)
                attributeMap[sanitizedKey] = attribute.Id;
            attributesCreated++;
        }

        // Check for existing mapping (localCategoryId + connectionId is unique)
        var categoryMapping = await _context.categoryMappings
            .Include(m => m.fieldMappings)
            .FirstOrDefaultAsync(m => m.localCategoryId == category.Id && m.connectionId == connection.Id, ct);

        bool reusingMapping = false;
        var existingFieldKeys = new HashSet<string>();
        int updatedFieldMappings = 0;

        // Remove meta-field from processing (plugin may still emit _hasSizes)
        fields = fields.Where(f => f.Key != "_hasSizes").ToList();

        if (categoryMapping != null)
        {
            // Existing mapping found — reuse it, update external category info
            categoryMapping.externalCategoryId = externalCategoryId;
            categoryMapping.externalCategoryName = externalCategoryName ?? localCategoryName;
            reusingMapping = true;

            // Collect already-mapped field keys so we skip them below (additive import)
            if (categoryMapping.fieldMappings != null)
            {
                foreach (var fm in categoryMapping.fieldMappings)
                    existingFieldKeys.Add(fm.externalFieldKey);

                // Update existing Static mappings that now have a matching CategoryAttribute.
                // This happens when a field was mapped as Static in a previous import
                // (because the user didn't select it for attribute creation), and now the
                // user imports that attribute — the mapping should auto-link to it.
                foreach (var fm in categoryMapping.fieldMappings)
                {
                    if (fm.sourceType == FieldMappingSourceType.Static
                        && fm.sourceAttributeId == null)
                    {
                        // Try raw key first, then sanitized key as fallback
                        if (attributeMap.TryGetValue(fm.externalFieldKey, out var attrId)
                            || attributeMap.TryGetValue(SanitizeKey(fm.externalFieldKey), out attrId))
                        {
                            fm.sourceType = FieldMappingSourceType.CategoryAttribute;
                            fm.sourceAttributeId = attrId;
                            updatedFieldMappings++;
                        }
                    }
                }
            }

            await _context.SaveChangesAsync(ct);
        }
        else
        {
            categoryMapping = new CategoryMapping();
            categoryMapping.AddFill(new CategoryMapping.AddForm
            {
                connectionId = connection.Id,
                localCategoryId = category.Id,
                externalCategoryId = externalCategoryId,
                externalCategoryName = externalCategoryName ?? localCategoryName
            });

            _context.categoryMappings.Add(categoryMapping);
            await _context.SaveChangesAsync(ct);
        }

        // Создаём маппинги полей с автоматическим сопоставлением
        // Start sort order after existing field mappings
        int fieldSortOrder = categoryMapping.fieldMappings?.Count ?? 0;
        int newFieldMappings = 0;
        foreach (var field in fields.OrderBy(f => f.SortOrder))
        {
            // Skip fields that already have a mapping (preserve user-configured mappings)
            if (existingFieldKeys.Contains(field.Key))
                continue;

            var fieldMapping = new FieldMapping();

            // Определяем автоматический источник данных
            var (sourceType, sourceField, sourceAttributeId) = DetermineAutoMapping(field, attributeMap);

            // Если поле маппится на локальное поле товара (description, name, etc.),
            // то не делаем его обязательным - данные берутся из локального поля
            var isRequired = sourceType == FieldMappingSourceType.ProductField
                ? false
                : field.IsRequired;

            fieldMapping.AddFill(new FieldMapping.AddForm
            {
                categoryMappingId = categoryMapping.Id,
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
                sortOrder = fieldSortOrder++
            });

            _context.fieldMappings.Add(fieldMapping);
            newFieldMappings++;
        }

        await _context.SaveChangesAsync(ct);

        var message = reusingMapping
            ? $"В маппинг '{localCategoryName}' добавлено {attributesCreated} атрибутов и {newFieldMappings} полей"
              + (updatedFieldMappings > 0 ? $", обновлено {updatedFieldMappings} маппингов" : "")
            : isExistingCategory
                ? $"В категорию '{localCategoryName}' добавлено {attributesCreated} атрибутов (+ {fields.Count - fieldsForAttributes.Count} базовых полей товара)"
                : $"Категория '{localCategoryName}' успешно импортирована с {attributesCreated} атрибутами (+ {fields.Count - fieldsForAttributes.Count} базовых полей товара)";

        return CategoryImportResult.Success(
            categoryMapping.Id,
            category.Id,
            attributesCreated,
            message
        );
    }

    /// <summary>
    /// Determine automatic field mapping based on field key/name patterns.
    /// </summary>
    internal static (FieldMappingSourceType sourceType, string? sourceField, int? sourceAttributeId)
        DetermineAutoMapping(ExternalField field, Dictionary<string, int> attributeMap)
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

        // По умолчанию маппим на созданный атрибут категории
        if (attributeMap.TryGetValue(field.Key, out var attributeId))
            return (FieldMappingSourceType.CategoryAttribute, null, attributeId);

        // Если ничего не подошло - статическое пустое значение
        return (FieldMappingSourceType.Static, null, null);
    }

    /// <summary>
    /// Check whether a field is a base product field (SKU, name, description, price, stock, images, grouping).
    /// These fields don't need attribute creation — they are handled by product fields or special mappings.
    /// </summary>
    internal static bool IsBaseOrSpecialField(ExternalField field)
    {
        var keyLower = field.Key.ToLowerInvariant();
        var nameLower = field.Name.ToLowerInvariant();
        return IsSkuField(keyLower, nameLower) ||
               IsNameField(keyLower, nameLower) ||
               IsDescriptionField(keyLower, nameLower) ||
               IsPriceField(keyLower, nameLower) ||
               IsStockField(keyLower, nameLower) ||
               IsGroupingField(field.Key, field.Name) ||
               IsImageField(field.Key, field.Name);
    }

    internal static bool IsSkuField(string key, string name)
    {
        // Ozon attribute 9024 = Артикул
        if (key == "9024") return true;
        if (key == "model" || name == "model") return true;

        var patterns = new[] { "sku", "артикул", "article", "vendor_code", "vendorcode" };
        return patterns.Any(p => key.Contains(p) || name.Contains(p));
    }

    internal static bool IsNameField(string key, string name)
    {
        // Ozon attribute 4180 = Название товара
        if (key == "4180") return true;

        // Используем Contains для русских паттернов, но только точные — "название товара", не просто "название"
        // (иначе матчатся "Название цвета" 10097, "Название группы" 22390 и т.д.)
        var exactPatterns = new[] { "name", "title" };
        var containsPatterns = new[] { "название товара", "наименование" };

        return exactPatterns.Any(p => key == p || name == p || key.StartsWith(p + "_") || key.EndsWith("_" + p)) ||
               containsPatterns.Any(p => name.Contains(p));
    }

    internal static bool IsDescriptionField(string key, string name)
    {
        // Ozon attribute 4191 = Аннотация (Description)
        if (key == "4191") return true;

        // "аннотация" и "описание" - одно и то же (annotation = description)
        var patterns = new[] { "description", "описание", "desc", "аннотация", "annotation" };
        return patterns.Any(p => key.Contains(p) || name.Contains(p));
    }

    internal static bool IsPriceField(string key, string name)
    {
        var patterns = new[] { "price", "цена", "стоимость", "cost" };
        return patterns.Any(p => key == p || name == p || key.StartsWith(p + "_"));
    }

    internal static bool IsStockField(string key, string name)
    {
        var keyLower = key.ToLowerInvariant();
        var nameLower = name.ToLowerInvariant();

        // Точное совпадение - эти слова явно означают остаток на складе
        var exactPatterns = new[] { "stock", "остаток", "остатки", "quantity", "count" };
        if (exactPatterns.Any(p => keyLower == p || nameLower == p))
            return true;

        // Паттерны с префиксом/суффиксом для stock
        if (keyLower.StartsWith("stock_") || keyLower.EndsWith("_stock"))
            return true;

        // НЕ используем Contains для "количество" - иначе "Количество фигурок" будет матчиться
        // Только если это явно поле остатков
        return false;
    }

    /// <summary>
    /// Determine a default value for a field based on its name and available options.
    /// For example, for "Brand" fields looks for "No brand" options.
    /// </summary>
    private string? DetermineDefaultValue(ExternalField field)
    {
        if (field.Options == null || field.Options.Count == 0)
            return null;

        var nameLower = field.Name.ToLowerInvariant();

        // Для полей "Бренд" / "Brand" ищем специальные значения
        if (nameLower.Contains("бренд") || nameLower.Contains("brand"))
        {
            var noBrandPatterns = new[]
            {
                "нет бренда",
                "без бренда",
                "не указан",
                "отсутствует",
                "no brand",
                "unbranded"
            };

            var noBrandOption = field.Options.FirstOrDefault(o =>
                noBrandPatterns.Any(p => o.Label.ToLowerInvariant().Contains(p)));

            if (noBrandOption != null)
                return noBrandOption.Value; // Возвращаем dictionary_value_id
        }

        // Для полей "Страна" / "Country" ищем "Россия" как значение по умолчанию
        if (nameLower.Contains("страна") || nameLower.Contains("country"))
        {
            var russiaOption = field.Options.FirstOrDefault(o =>
                o.Label.ToLowerInvariant().Contains("россия") ||
                o.Label.ToLowerInvariant() == "russia");

            if (russiaOption != null)
                return russiaOption.Value;
        }

        return null;
    }

    /// <summary>
    /// Check whether the field is a product grouping field.
    /// Such fields should not create attributes; they map to group name instead.
    /// </summary>
    internal static bool IsGroupingField(string key, string name)
    {
        // Ozon 9048 = "Название модели (для объединения в одну карточку)"
        // Ozon 22390 = "Название группы"
        if (key == "9048" || key == "22390") return true;

        var nameLower = name.ToLowerInvariant();

        // Паттерны для определения полей объединения
        if (nameLower.Contains("для объединения") ||
            nameLower.Contains("объединение в карточку") ||
            nameLower.Contains("группировка товаров") ||
            nameLower.Contains("название группы"))
            return true;

        return false;
    }

    /// <summary>
    /// Check whether the field is an image field.
    /// Image fields should not create attributes; they are handled separately.
    /// </summary>
    internal static bool IsImageField(string key, string name)
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

    /// <summary>
    /// Determine attribute group by keywords in the name.
    /// Used as a fallback when the marketplace does not provide a group.
    /// </summary>
    private string? DetermineGroupByKeywords(string name)
    {
        var nameLower = name.ToLowerInvariant();

        // Размеры и вес
        var sizeWeightPatterns = new[] { "размер", "ширин", "высот", "глубин", "длин", "вес", "масса",
            "weight", "height", "width", "depth", "length", "size", "dimension" };
        if (sizeWeightPatterns.Any(p => nameLower.Contains(p)))
            return "Размеры и вес";

        // Внешний вид
        var appearancePatterns = new[] { "цвет", "color", "colour", "материал", "material",
            "оттенок", "тон", "покрытие", "текстур", "фактур" };
        if (appearancePatterns.Any(p => nameLower.Contains(p)))
            return "Внешний вид";

        // Медиа (обычно фильтруется IsImageField, но на всякий случай)
        var mediaPatterns = new[] { "изображ", "фото", "видео", "image", "photo", "video",
            "картинк", "media", "preview" };
        if (mediaPatterns.Any(p => nameLower.Contains(p)))
            return "Медиа";

        // Упаковка
        var packagePatterns = new[] { "упаков", "package", "коробк", "тара", "комплект",
            "набор", "box", "pack", "bundle" };
        if (packagePatterns.Any(p => nameLower.Contains(p)))
            return "Упаковка";

        // Маркировка
        var labelPatterns = new[] { "штрихкод", "ean", "barcode", "артикул", "sku", "код",
            "gtin", "upc", "isbn", "маркировк" };
        if (labelPatterns.Any(p => nameLower.Contains(p)))
            return "Маркировка";

        // Бренд и производитель
        var brandPatterns = new[] { "бренд", "brand", "производител", "manufacturer",
            "изготовител", "торговая марка" };
        if (brandPatterns.Any(p => nameLower.Contains(p)))
            return "Бренд";

        // Страна
        var countryPatterns = new[] { "страна", "country", "происхожден" };
        if (countryPatterns.Any(p => nameLower.Contains(p)))
            return "Страна";

        // Не удалось определить группу - вернём null (будет отображаться как "Прочее" на UI)
        return null;
    }

    /// <summary>
    /// Sanitize an attribute key by removing special characters.
    /// </summary>
    private string SanitizeKey(string key)
    {
        // Оставляем только буквы, цифры и подчёркивания
        var sanitized = new string(key.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        // Ограничиваем длину
        if (sanitized.Length > CategoryAttribute.ATTRIBUTE_KEY_MAX_LENGTH)
            sanitized = sanitized.Substring(0, CategoryAttribute.ATTRIBUTE_KEY_MAX_LENGTH);

        // Если пустой - генерируем случайный
        if (string.IsNullOrEmpty(sanitized))
            sanitized = $"attr_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        return sanitized;
    }

    /// <summary>
    /// Map an external field type string to a local <see cref="AttributeType"/> enum value.
    /// </summary>
    private AttributeType MapExternalFieldType(string externalType)
    {
        return externalType.ToLowerInvariant() switch
        {
            "number" or "integer" or "decimal" or "float" => AttributeType.Number,
            "boolean" or "bool" => AttributeType.Boolean,
            "select" or "enum" => AttributeType.Select,
            "multiselect" or "multi" or "array" => AttributeType.MultiSelect,
            _ => AttributeType.Text
        };
    }

    /// <summary>
    /// Serialize field options to JSON format.
    /// Format: [{"value":"123","label":"Option 1"}, ...].
    /// </summary>
    private string SerializeOptions(List<ExternalFieldOption> options)
    {
        var optionsList = options.Select(o => new { value = o.Value, label = o.Label }).ToList();
        return JsonSerializer.Serialize(optionsList);
    }
}

/// <summary>
/// Result of a category import operation.
/// </summary>
public class CategoryImportResult
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? CategoryMappingId { get; private set; }
    public int? LocalCategoryId { get; private set; }
    public int AttributesCreated { get; private set; }
    public string? Message { get; private set; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static CategoryImportResult Success(int categoryMappingId, int localCategoryId, int attributesCreated, string message)
    {
        return new CategoryImportResult
        {
            IsSuccess = true,
            CategoryMappingId = categoryMappingId,
            LocalCategoryId = localCategoryId,
            AttributesCreated = attributesCreated,
            Message = message
        };
    }

    /// <summary>
    /// Create a failure result with an error message.
    /// </summary>
    public static CategoryImportResult Failure(string errorMessage)
    {
        return new CategoryImportResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
