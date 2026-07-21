namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Product price information on the marketplace.
/// </summary>
/// <param name="ExternalProductId">Product ID on the marketplace.</param>
/// <param name="Price">Current price.</param>
/// <param name="OldPrice">Old price (crossed out).</param>
/// <param name="Stock">Stock (optional, may be returned together with price).</param>
/// <param name="DiscountedPrice">Discounted price.</param>
/// <param name="Currency">Currency (ISO code).</param>
/// <param name="VariantId">Variant identifier for multi-size products (e.g. chrtId for WB).</param>
/// <param name="MinPrice">Minimum price (floor price for marketplace discounts, e.g. Ozon min_price).</param>
/// <param name="ExternalSku">Seller's article / offer_id - used as identifier fallback when ExternalProductId is unavailable.</param>
/// <param name="Discount">Discount percent (0-99) for marketplaces that use base price + discount (e.g. Wildberries).</param>
/// <param name="NetPrice">Net/cost price (Ozon net_price).</param>
/// <param name="AutoActionEnabled">Ozon auto_action_enabled: UNKNOWN, ENABLED, DISABLED.</param>
/// <param name="PriceStrategyEnabled">Ozon price_strategy_enabled: UNKNOWN, ENABLED, DISABLED.</param>
/// <param name="MinPriceForAutoActionsEnabled">Ozon min_price_for_auto_actions_enabled.</param>
public record PriceInfo(
    string ExternalProductId,
    decimal? Price,
    decimal? OldPrice = null,
    int? Stock = null,
    decimal? DiscountedPrice = null,
    string? Currency = null,
    string? VariantId = null,
    decimal? MinPrice = null,
    string? ExternalSku = null,
    int? Discount = null,
    decimal? NetPrice = null,
    string? AutoActionEnabled = null,
    string? PriceStrategyEnabled = null,
    bool? MinPriceForAutoActionsEnabled = null
);

/// <summary>
/// Product stock information.
/// </summary>
/// <param name="ExternalProductId">Product ID on the marketplace.</param>
/// <param name="Quantity">Stock quantity.</param>
/// <param name="WarehouseId">Warehouse ID (if applicable).</param>
/// <param name="WarehouseName">Warehouse name.</param>
/// <param name="VariantId">Variant identifier for multi-size products (e.g. chrtId for WB).</param>
/// <param name="Barcode">Barcode associated with this variant.</param>
public record StockInfo(
    string ExternalProductId,
    int Quantity,
    string? WarehouseId = null,
    string? WarehouseName = null,
    string? VariantId = null,
    string? Barcode = null
);
