namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Product from an external marketplace
/// </summary>
public record ExternalProduct(
    /// <summary>
    /// Product identifier on the marketplace
    /// </summary>
    string ExternalId,

    /// <summary>
    /// Article / SKU
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
    /// Category ID on the marketplace
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
    /// Additional marketplace data
    /// </summary>
    Dictionary<string, object>? RawData = null,

    /// <summary>
    /// Group ID on the marketplace (for combining products into a single listing)
    /// </summary>
    string? GroupId = null,

    /// <summary>
    /// Group name on the marketplace
    /// </summary>
    string? GroupName = null,

    /// <summary>
    /// Product variants (e.g. sizes) within a single marketplace card.
    /// Used for WB-style marketplaces where one card has multiple sizes.
    /// Null for single-variant products or Ozon (each size = separate product).
    /// </summary>
    List<ExternalProductVariant>? Variants = null
);
