using PrintO.Enums;
using PrintO.Infrastructure.Storage;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Products;
using PrintO.Services.Marketplace.Modifiers;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrintO.Services.Marketplace;

/// <summary>
/// Service for building export data based on field mappings
/// </summary>
public class MappingService
{
    private readonly DataContext _context;
    private readonly IServiceProvider _serviceProvider;

    public MappingService(DataContext context, IServiceProvider serviceProvider)
    {
        _context = context;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Build export data for a product based on field mappings
    /// </summary>
    /// <param name="product">Product with loaded relations</param>
    /// <param name="categoryMapping">Category mapping with loaded fieldMappings</param>
    /// <returns>Dictionary: marketplace field key -> value</returns>
    public Task<Dictionary<string, object?>> BuildExportDataAsync(
        Product product,
        CategoryMapping categoryMapping,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, object?>();
        var fieldTypes = new Dictionary<string, string>();

        // Get all active field mappings
        var fieldMappings = categoryMapping.fieldMappings
            .Where(fm => fm.isEnabled)
            .OrderBy(fm => fm.sortOrder)
            .ToList();

        foreach (var mapping in fieldMappings)
        {
            var value = GetFieldValue(product, mapping);

            // Deserialize multiselect values BEFORE modifiers,
            // so modifiers work with List<string> instead of raw JSON/CSV strings
            if (mapping.externalFieldType == "multiselect" && value is string multiStr
                && !string.IsNullOrEmpty(multiStr))
            {
                if (multiStr.StartsWith('['))
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(multiStr);
                        if (list != null && list.Count > 0)
                            value = list;
                    }
                    catch (JsonException) { }
                }
                else if (multiStr.Contains(','))
                {
                    var parts = multiStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length > 1)
                        value = parts.ToList();
                }
            }

            // Build modifier context for template resolution
            var modifierContext = new ModifierContext
            {
                ResolveReference = (reference) => ResolveFieldReference(product, reference),
                SourceAttributeOptions = ResolveSourceOptions(product, mapping)
            };

            // Apply modifiers: prefer modifiersJson, fall back to legacy transformExpression
            if (!string.IsNullOrEmpty(mapping.modifiersJson))
            {
                value = ModifierPipeline.ApplyModifiers(value, mapping.modifiersJson, modifierContext);
            }
            else if (!string.IsNullOrEmpty(mapping.transformExpression))
            {
                value = ApplyTransform(value, mapping.transformExpression);
            }

            // Coerce value to match the effective field type (forceType overrides externalFieldType)
            var effectiveType = GetEffectiveFieldType(mapping.modifiersJson, mapping.externalFieldType);
            value = CoerceToFieldType(value, effectiveType);
            if (!string.IsNullOrEmpty(effectiveType))
                fieldTypes[mapping.externalFieldKey] = effectiveType;

            result[mapping.externalFieldKey] = value;
        }

        // Attach field type metadata for the plugin
        result["_fieldTypes"] = fieldTypes;

        return Task.FromResult(result);
    }

    /// <summary>
    /// Get field value from the data source.
    /// Public to allow preview endpoint to access source values.
    /// </summary>
    public object? GetFieldValue(
        Product product,
        FieldMapping mapping)
    {
        return mapping.sourceType switch
        {
            FieldMappingSourceType.ProductField => GetProductFieldValue(product, mapping.sourceField),
            FieldMappingSourceType.CategoryAttribute => GetCategoryAttributeValue(product, mapping.sourceAttributeId),
            FieldMappingSourceType.StoreAttribute => GetStoreAttributeValue(product, mapping.sourceAttributeId),
            FieldMappingSourceType.Static => mapping.staticValue,
            FieldMappingSourceType.GroupName => GetGroupNameValue(product),
            _ => null
        };
    }

    /// <summary>
    /// Resolve a template reference like "Name", "attr:5", "storeAttr:3" to a product value.
    /// </summary>
    public object? ResolveFieldReference(Product product, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        // attr:ID — category attribute
        if (reference.StartsWith("attr:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(reference[5..], out var attrId))
                return GetCategoryAttributeValue(product, attrId);
            return null;
        }

        // storeAttr:ID — store attribute
        if (reference.StartsWith("storeAttr:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(reference[10..], out var storeAttrId))
                return GetStoreAttributeValue(product, storeAttrId);
            return null;
        }

        // Plain field name — product field (case-insensitive)
        return GetProductFieldValue(product, reference);
    }

    /// <summary>
    /// Get value of a standard product field
    /// </summary>
    private object? GetProductFieldValue(Product product, string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            return null;

        return fieldName.ToLowerInvariant() switch
        {
            "sku" => product.sku,
            "barcode" => product.barcode,
            "name" => product.name,
            "description" => product.description,
            "price" => product.price,
            "storagequantity" or "stock" or "quantity" => product.storageQuantity,
            "categoryid" => product.categoryId,
            "categoryname" => product.category?.name,
            "groupname" => GetGroupNameValue(product),
            "images" => GetProductImageUrlsAsync(product).GetAwaiter().GetResult(),
            _ => null
        };
    }

    /// <summary>
    /// Get the list of product image URLs in the correct order
    /// </summary>
    private async Task<List<string>> GetProductImageUrlsAsync(Product product)
    {
        var urls = new List<string>();

        if (product.images == null || !product.images.Any())
            return urls;

        // Get images ordered by index
        var orderedImages = product.images
            .OrderBy(i => i.index)
            .ToList();

        foreach (var imageRef in orderedImages)
        {
            // Use the file navigation property directly if available
            var file = imageRef.file ?? product.files?.FirstOrDefault(f => f.Id == imageRef.fileId);
            if (file != null)
            {
                var storageService = _serviceProvider.GetRequiredService<IStorageService>();
                var url = await storageService.GetPublicPresignedUrlAsync(file.filePath, 3600);
                if (!string.IsNullOrEmpty(url))
                {
                    urls.Add(url);
                }
            }
        }

        return urls;
    }

    /// <summary>
    /// Get category attribute value
    /// </summary>
    private object? GetCategoryAttributeValue(Product product, int? attributeId)
    {
        if (!attributeId.HasValue)
            return null;

        var attributeValue = product.attributeValues?
            .FirstOrDefault(av => av.attributeId == attributeId.Value);

        return attributeValue?.value;
    }

    /// <summary>
    /// Get store attribute value
    /// </summary>
    private object? GetStoreAttributeValue(Product product, int? attributeId)
    {
        if (!attributeId.HasValue)
            return null;

        var attributeValue = product.storeAttributeValues?
            .FirstOrDefault(sav => sav.storeAttributeId == attributeId.Value);

        return attributeValue?.value;
    }

    /// <summary>
    /// Get the product group name.
    /// Used for grouping fields on marketplaces (e.g. Ozon field 9048).
    /// </summary>
    private object? GetGroupNameValue(Product product)
    {
        // Get the first product group (a product can belong to multiple groups)
        var groupMember = product.groupMemberships?.FirstOrDefault();
        if (groupMember?.group != null)
        {
            return groupMember.group.name;
        }

        return null;
    }

    /// <summary>
    /// Coerce a value to match the expected field type.
    /// </summary>
    private static object? CoerceToFieldType(object? value, string? fieldType)
    {
        if (value == null || string.IsNullOrEmpty(fieldType))
            return value;

        return fieldType.ToLowerInvariant() switch
        {
            "string" or "text" => value is System.Collections.IEnumerable && value is not string
                ? value
                : value.ToString(),
            "number" => ToDecimalOrPassthrough(value),
            "integer" => ToLongOrPassthrough(value),
            "boolean" => ToBoolOrPassthrough(value),
            _ => value // select, multiselect, array — no coercion
        };
    }

    private static object? ToDecimalOrPassthrough(object? value)
    {
        if (value is decimal) return value;
        if (decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        return value;
    }

    private static object? ToLongOrPassthrough(object? value)
    {
        if (value is long) return value;
        if (value is int i) return (long)i;
        if (long.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
            return l;
        // Try parsing as decimal first, then truncate (e.g. "123.45" → 123)
        if (decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return (long)Math.Truncate(d);
        return value;
    }

    private static object? ToBoolOrPassthrough(object? value)
    {
        if (value is bool) return value;
        var str = value?.ToString()?.Trim().ToLowerInvariant();
        return str switch
        {
            "true" or "1" or "yes" or "да" => true,
            "false" or "0" or "no" or "нет" => false,
            _ => value
        };
    }

    /// <summary>
    /// Get the effective field type — checks for a forceType modifier override, falls back to defaultType.
    /// </summary>
    private static string? GetEffectiveFieldType(string? modifiersJson, string? defaultType)
    {
        if (string.IsNullOrWhiteSpace(modifiersJson))
            return defaultType;

        try
        {
            using var doc = JsonDocument.Parse(modifiersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return defaultType;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                // Skip disabled modifiers
                if (element.TryGetProperty("enabled", out var enabledProp)
                    && enabledProp.ValueKind == JsonValueKind.False)
                    continue;

                if (element.TryGetProperty("type", out var typeProp) &&
                    typeProp.GetString()?.Equals("forceType", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (element.TryGetProperty("params", out var paramsProp) &&
                        paramsProp.TryGetProperty("type", out var forceTypeProp))
                    {
                        var forceType = forceTypeProp.GetString();
                        if (!string.IsNullOrEmpty(forceType))
                            return forceType;
                    }
                }
            }
        }
        catch (JsonException) { }

        return defaultType;
    }

    /// <summary>
    /// Resolve options from the source attribute (for select/multiselect toText modifier).
    /// </summary>
    private List<Modifiers.SourceOption>? ResolveSourceOptions(Product product, FieldMapping mapping)
    {
        string? optionsJson = null;

        if (mapping.sourceType == FieldMappingSourceType.CategoryAttribute && mapping.sourceAttributeId.HasValue)
        {
            optionsJson = product.attributeValues?
                .FirstOrDefault(av => av.attributeId == mapping.sourceAttributeId.Value)
                ?.attribute?.options;
        }
        else if (mapping.sourceType == FieldMappingSourceType.StoreAttribute && mapping.sourceAttributeId.HasValue)
        {
            optionsJson = product.storeAttributeValues?
                .FirstOrDefault(sav => sav.storeAttributeId == mapping.sourceAttributeId.Value)
                ?.storeAttribute?.options;
        }

        if (string.IsNullOrEmpty(optionsJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<Modifiers.SourceOption>>(optionsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            // Fallback: comma-separated "value:label" format
            try
            {
                return optionsJson.Split(',')
                    .Select(o => o.Trim())
                    .Where(o => !string.IsNullOrEmpty(o))
                    .Select(o =>
                    {
                        var parts = o.Split(':', 2);
                        return new Modifiers.SourceOption
                        {
                            value = parts[0].Trim(),
                            label = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim()
                        };
                    })
                    .ToList();
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Apply a transformation to a value.
    /// Format: "multiply:1000", "append:_suffix", "prepend:prefix_", "replace:old:new", "uppercase", "lowercase"
    /// </summary>
    private object? ApplyTransform(object value, string transformExpression)
    {
        var parts = transformExpression.Split(':', 2);
        var operation = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : null;

        try
        {
            return operation switch
            {
                "multiply" when decimal.TryParse(argument, out var factor) =>
                    Convert.ToDecimal(value) * factor,

                "divide" when decimal.TryParse(argument, out var divisor) && divisor != 0 =>
                    Convert.ToDecimal(value) / divisor,

                "add" when decimal.TryParse(argument, out var addend) =>
                    Convert.ToDecimal(value) + addend,

                "append" => value.ToString() + argument,

                "prepend" => argument + value.ToString(),

                "replace" when argument != null =>
                    ApplyReplace(value.ToString() ?? "", argument),

                "uppercase" => value.ToString()?.ToUpperInvariant(),

                "lowercase" => value.ToString()?.ToLowerInvariant(),

                "trim" => value.ToString()?.Trim(),

                "round" when int.TryParse(argument, out var decimals) =>
                    Math.Round(Convert.ToDecimal(value), decimals),

                "floor" => Math.Floor(Convert.ToDecimal(value)),

                "ceiling" => Math.Ceiling(Convert.ToDecimal(value)),

                "truncate" when int.TryParse(argument, out var maxLength) =>
                    TruncateString(value.ToString(), maxLength),

                _ => value
            };
        }
        catch
        {
            // If the transformation failed, return the original value
            return value;
        }
    }

    /// <summary>
    /// Apply a replace transformation.
    /// Argument format: "old:new"
    /// </summary>
    private string ApplyReplace(string value, string argument)
    {
        var replaceParts = argument.Split(':', 2);
        if (replaceParts.Length == 2)
        {
            return value.Replace(replaceParts[0], replaceParts[1]);
        }
        return value;
    }

    /// <summary>
    /// Truncate a string to the specified length
    /// </summary>
    private string? TruncateString(string? value, int maxLength)
    {
        if (value == null || value.Length <= maxLength)
            return value;
        return value.Substring(0, maxLength);
    }

    /// <summary>
    /// Get available product fields for mapping
    /// </summary>
    public static List<ProductFieldInfo> GetAvailableProductFields()
    {
        return new List<ProductFieldInfo>
        {
            new("sku", "SKU / Артикул", "string"),
            new("barcode", "Штрихкод (EAN)", "string"),
            new("name", "Название", "string"),
            new("description", "Описание", "string"),
            new("price", "Цена", "number"),
            new("storageQuantity", "Количество на складе", "number"),
            new("categoryName", "Название категории", "string"),
            new("groupName", "Название группы товара", "string"),
            new("images", "Изображения", "array")
        };
    }
}

/// <summary>
/// Information about an available product field for mapping
/// </summary>
public record ProductFieldInfo(
    string Key,
    string DisplayName,
    string Type
);
