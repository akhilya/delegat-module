namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Product price information on the marketplace
/// </summary>
public record PriceInfo(
    /// <summary>
    /// Product ID on the marketplace
    /// </summary>
    string ExternalProductId,

    /// <summary>
    /// Current price
    /// </summary>
    decimal? Price,

    /// <summary>
    /// Old price (crossed out)
    /// </summary>
    decimal? OldPrice = null,

    /// <summary>
    /// Stock (optional, may be returned together with price)
    /// </summary>
    int? Stock = null,

    /// <summary>
    /// Discounted price
    /// </summary>
    decimal? DiscountedPrice = null,

    /// <summary>
    /// Currency (ISO code)
    /// </summary>
    string Currency = "RUB",

    /// <summary>
    /// Variant identifier for multi-size products (e.g. chrtId for WB).
    /// </summary>
    string? VariantId = null
);

/// <summary>
/// Product stock information
/// </summary>
public record StockInfo(
    /// <summary>
    /// Product ID on the marketplace
    /// </summary>
    string ExternalProductId,

    /// <summary>
    /// Stock quantity
    /// </summary>
    int Quantity,

    /// <summary>
    /// Warehouse ID (if applicable)
    /// </summary>
    string? WarehouseId = null,

    /// <summary>
    /// Warehouse name
    /// </summary>
    string? WarehouseName = null,

    /// <summary>
    /// Variant identifier for multi-size products (e.g. chrtId for WB).
    /// </summary>
    string? VariantId = null,

    /// <summary>
    /// Barcode associated with this variant.
    /// </summary>
    string? Barcode = null
);
