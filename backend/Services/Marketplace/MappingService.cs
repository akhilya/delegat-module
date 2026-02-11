using PrintO.Enums;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Products;
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
            // Track field types so the plugin knows how to handle each value
            if (!string.IsNullOrEmpty(mapping.externalFieldType))
                fieldTypes[mapping.externalFieldKey] = mapping.externalFieldType;

            var value = GetFieldValue(product, mapping);

            // Apply transformation if specified
            if (!string.IsNullOrEmpty(mapping.transformExpression) && value != null)
            {
                value = ApplyTransform(value, mapping.transformExpression);
            }

            // Multiselect values may be stored as JSON arrays '["123","456"]'
            // or as comma-separated strings "123,456" (legacy format from field mapping page).
            // Deserialize to List<string> so the plugin receives a proper list.
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

            result[mapping.externalFieldKey] = value;
        }

        // Attach field type metadata for the plugin
        result["_fieldTypes"] = fieldTypes;

        return Task.FromResult(result);
    }

    /// <summary>
    /// Get field value from the data source
    /// </summary>
    private object? GetFieldValue(
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
            "images" => GetProductImageUrls(product),
            _ => null
        };
    }

    /// <summary>
    /// Get the list of product image URLs in the correct order
    /// </summary>
    private List<string> GetProductImageUrls(Product product)
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
                var url = file.GetFullPath(_serviceProvider);
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
