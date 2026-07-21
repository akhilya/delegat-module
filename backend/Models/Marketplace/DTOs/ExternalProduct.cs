namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Product from an external marketplace.
/// </summary>
/// <param name="ExternalId">Product identifier on the marketplace.</param>
/// <param name="Sku">Article / SKU.</param>
/// <param name="Name">Product name.</param>
/// <param name="Description">Description.</param>
/// <param name="Price">Price.</param>
/// <param name="Quantity">Stock quantity.</param>
/// <param name="CategoryId">Category ID on the marketplace.</param>
/// <param name="CategoryName">Category name.</param>
/// <param name="Attributes">Attribute values (key -&gt; value).</param>
/// <param name="ImageUrls">Product image URLs.</param>
/// <param name="Barcode">Barcode.</param>
/// <param name="Brand">Brand.</param>
/// <param name="RawData">Additional marketplace data.</param>
/// <param name="GroupId">Group ID on the marketplace (for combining products into a single listing).</param>
/// <param name="GroupName">Group name on the marketplace.</param>
/// <param name="Variants">Product variants (e.g. sizes) within a single marketplace card. Null for single-variant products.</param>
public record ExternalProduct(
    string ExternalId,
    string? Sku,
    string Name,
    string? Description = null,
    decimal? Price = null,
    int? Quantity = null,
    string? CategoryId = null,
    string? CategoryName = null,
    Dictionary<string, object>? Attributes = null,
    List<string>? ImageUrls = null,
    string? Barcode = null,
    string? Brand = null,
    Dictionary<string, object>? RawData = null,
    string? GroupId = null,
    string? GroupName = null,
    List<ExternalProductVariant>? Variants = null
);
