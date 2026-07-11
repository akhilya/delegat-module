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
    string? VariantId = null,

    /// <summary>
    /// Minimum price (floor price for marketplace discounts, e.g. Ozon min_price).
    /// </summary>
    decimal? MinPrice = null,

    /// <summary>
    /// Seller's article / offer_id — used as identifier fallback when ExternalProductId is unavailable.
    /// </summary>
    string? ExternalSku = null,

    /// <summary>
    /// Discount percent (0–99) for marketplaces that use base price + discount (e.g. Wildberries)
    /// </summary>
    int? Discount = null,

    /// <summary>
    /// Net/cost price (Ozon net_price)
    /// </summary>
    decimal? NetPrice = null,

    /// <summary>
    /// Ozon auto_action_enabled: UNKNOWN, ENABLED, DISABLED
    /// </summary>
    string? AutoActionEnabled = null,

    /// <summary>
    /// Ozon price_strategy_enabled: UNKNOWN, ENABLED, DISABLED
    /// </summary>
    string? PriceStrategyEnabled = null,

    /// <summary>
    /// Ozon min_price_for_auto_actions_enabled
    /// </summary>
    bool? MinPriceForAutoActionsEnabled = null
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
