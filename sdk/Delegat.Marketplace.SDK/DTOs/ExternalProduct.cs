namespace Delegat.Marketplace.SDK.DTOs;

/// <summary>
/// Product from external marketplace
/// </summary>
public record ExternalProduct(
    /// <summary>
    /// Product ID on marketplace
    /// </summary>
    string ExternalId,

    /// <summary>
    /// SKU/article
    /// </summary>
    string? Sku,

    /// <summary>
    /// Product name
    /// </summary>
    string Name,

    /// <summary>
    /// Description
    /// </summary>
    string? Description = null,

    /// <summary>
    /// Price
    /// </summary>
    decimal? Price = null,

    /// <summary>
    /// Stock quantity
    /// </summary>
    int? Quantity = null,

    /// <summary>
    /// Category ID on marketplace
    /// </summary>
    string? CategoryId = null,

    /// <summary>
    /// Category name
    /// </summary>
    string? CategoryName = null,

    /// <summary>
    /// Attribute values (key -> value)
    /// </summary>
    Dictionary<string, object>? Attributes = null,

    /// <summary>
    /// Product image URLs
    /// </summary>
    List<string>? ImageUrls = null,

    /// <summary>
    /// Barcode
    /// </summary>
    string? Barcode = null,

    /// <summary>
    /// Brand
    /// </summary>
    string? Brand = null,

    /// <summary>
    /// Raw marketplace data
    /// </summary>
    Dictionary<string, object>? RawData = null,

    /// <summary>
    /// External group ID on marketplace (for product grouping/consolidation)
    /// </summary>
    string? GroupId = null,

    /// <summary>
    /// Group name on marketplace
    /// </summary>
    string? GroupName = null,

    /// <summary>
    /// Product variants (e.g. sizes) within a single marketplace card.
    /// Used for WB-style marketplaces where one card has multiple sizes.
    /// Null for single-variant products or Ozon (each size = separate product).
    /// </summary>
    List<ExternalProductVariant>? Variants = null
);
