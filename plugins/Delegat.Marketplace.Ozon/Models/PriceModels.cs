using System.Text.Json.Serialization;

namespace Delegat.Marketplace.Ozon.Models;

#region Request Models

/// <summary>
/// Request for /v5/product/info/prices
/// </summary>
public class PriceInfoRequest
{
    [JsonPropertyName("filter")]
    public PriceInfoFilter? Filter { get; set; }

    [JsonPropertyName("cursor")]
    public string Cursor { get; set; } = "";

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 100;
}

public class PriceInfoFilter
{
    [JsonPropertyName("offer_id")]
    public List<string>? OfferId { get; set; }

    [JsonPropertyName("product_id")]
    public List<string>? ProductId { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "ALL";
}

/// <summary>
/// Request for /v1/product/import/prices
/// </summary>
public class PriceImportRequest
{
    [JsonPropertyName("prices")]
    public List<PriceImportItem> Prices { get; set; } = new();
}

public class PriceImportItem
{
    [JsonPropertyName("auto_action_enabled")]
    public string? AutoActionEnabled { get; set; }

    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = "RUB";

    [JsonPropertyName("min_price")]
    public string? MinPrice { get; set; }

    [JsonPropertyName("offer_id")]
    public string? OfferId { get; set; }

    [JsonPropertyName("old_price")]
    public string? OldPrice { get; set; }

    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    [JsonPropertyName("price_strategy_enabled")]
    public string? PriceStrategyEnabled { get; set; }

    [JsonPropertyName("net_price")]
    public string? NetPrice { get; set; }

    [JsonPropertyName("min_price_for_auto_actions_enabled")]
    public bool? MinPriceForAutoActionsEnabled { get; set; }

    [JsonPropertyName("product_id")]
    public long? ProductId { get; set; }
}

#endregion

#region Response Models

/// <summary>
/// Response from /v5/product/info/prices
/// </summary>
public class PriceInfoResponse
{
    [JsonPropertyName("items")]
    public List<PriceInfoItem> Items { get; set; } = new();

    [JsonPropertyName("cursor")]
    public string Cursor { get; set; } = "";

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class PriceInfoItem
{
    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("acquiring")]
    public decimal Acquiring { get; set; }

    [JsonPropertyName("price")]
    public PriceDetails Price { get; set; } = new();

    [JsonPropertyName("price_indexes")]
    public PriceIndexes? PriceIndexes { get; set; }

    [JsonPropertyName("commissions")]
    public Commissions? Commissions { get; set; }

    [JsonPropertyName("marketing_actions")]
    public MarketingActions? MarketingActions { get; set; }

    [JsonPropertyName("volume_weight")]
    public decimal VolumeWeight { get; set; }
}

public class PriceDetails
{
    [JsonPropertyName("price")]
    public string CurrentPrice { get; set; } = "0";

    [JsonPropertyName("old_price")]
    public string OldPrice { get; set; } = "0";

    [JsonPropertyName("premium_price")]
    public string PremiumPrice { get; set; } = "0";

    [JsonPropertyName("recommended_price")]
    public string RecommendedPrice { get; set; } = "0";

    [JsonPropertyName("retail_price")]
    public string RetailPrice { get; set; } = "0";

    [JsonPropertyName("vat")]
    public string Vat { get; set; } = "0";

    [JsonPropertyName("min_ozon_price")]
    public string MinOzonPrice { get; set; } = "0";

    [JsonPropertyName("marketing_price")]
    public string MarketingPrice { get; set; } = "0";

    [JsonPropertyName("marketing_seller_price")]
    public string MarketingSellerPrice { get; set; } = "0";

    [JsonPropertyName("min_price")]
    public string MinPrice { get; set; } = "0";

    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = "RUB";

    [JsonPropertyName("auto_action_enabled")]
    public bool AutoActionEnabled { get; set; }

    [JsonPropertyName("price_strategy_enabled")]
    public bool PriceStrategyEnabled { get; set; }
}

public class Commissions
{
    [JsonPropertyName("sales_percent")]
    public decimal SalesPercent { get; set; }

    [JsonPropertyName("sales_percent_fbo")]
    public decimal SalesPercentFbo { get; set; }

    [JsonPropertyName("sales_percent_fbs")]
    public decimal SalesPercentFbs { get; set; }

    [JsonPropertyName("fbo_fulfillment_amount")]
    public decimal FboFulfillmentAmount { get; set; }

    [JsonPropertyName("fbo_direct_flow_trans_min_amount")]
    public decimal FboDirectFlowTransMinAmount { get; set; }

    [JsonPropertyName("fbo_direct_flow_trans_max_amount")]
    public decimal FboDirectFlowTransMaxAmount { get; set; }

    [JsonPropertyName("fbo_deliv_to_customer_amount")]
    public decimal FboDelivToCustomerAmount { get; set; }

    [JsonPropertyName("fbo_return_flow_amount")]
    public decimal FboReturnFlowAmount { get; set; }

    [JsonPropertyName("fbo_return_flow_trans_min_amount")]
    public decimal FboReturnFlowTransMinAmount { get; set; }

    [JsonPropertyName("fbo_return_flow_trans_max_amount")]
    public decimal FboReturnFlowTransMaxAmount { get; set; }

    [JsonPropertyName("fbs_first_mile_min_amount")]
    public decimal FbsFirstMileMinAmount { get; set; }

    [JsonPropertyName("fbs_first_mile_max_amount")]
    public decimal FbsFirstMileMaxAmount { get; set; }

    [JsonPropertyName("fbs_direct_flow_trans_min_amount")]
    public decimal FbsDirectFlowTransMinAmount { get; set; }

    [JsonPropertyName("fbs_direct_flow_trans_max_amount")]
    public decimal FbsDirectFlowTransMaxAmount { get; set; }

    [JsonPropertyName("fbs_deliv_to_customer_amount")]
    public decimal FbsDelivToCustomerAmount { get; set; }

    [JsonPropertyName("fbs_return_flow_amount")]
    public decimal FbsReturnFlowAmount { get; set; }

    [JsonPropertyName("fbs_return_flow_trans_min_amount")]
    public decimal FbsReturnFlowTransMinAmount { get; set; }

    [JsonPropertyName("fbs_return_flow_trans_max_amount")]
    public decimal FbsReturnFlowTransMaxAmount { get; set; }
}

public class MarketingActions
{
    [JsonPropertyName("date_from")]
    public DateTime? DateFrom { get; set; }

    [JsonPropertyName("date_to")]
    public DateTime? DateTo { get; set; }

    [JsonPropertyName("discount_value")]
    public decimal DiscountValue { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("actions")]
    public List<MarketingAction>? Actions { get; set; }
}

public class MarketingAction
{
    [JsonPropertyName("date_from")]
    public DateTime? DateFrom { get; set; }

    [JsonPropertyName("date_to")]
    public DateTime? DateTo { get; set; }

    [JsonPropertyName("discount_value")]
    public decimal DiscountValue { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
}

/// <summary>
/// Response from /v1/product/import/prices
/// </summary>
public class PriceImportResponse
{
    [JsonPropertyName("result")]
    public List<PriceImportResultItem> Result { get; set; } = new();
}

public class PriceImportResultItem
{
    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("updated")]
    public bool Updated { get; set; }

    [JsonPropertyName("errors")]
    public List<PriceImportError>? Errors { get; set; }
}

public class PriceImportError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

#endregion
