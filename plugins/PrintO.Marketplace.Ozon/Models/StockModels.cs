using System.Text.Json.Serialization;

namespace PrintO.Marketplace.Ozon.Models;

#region Request Models

/// <summary>
/// Request for /v4/product/info/stocks
/// </summary>
public class StockInfoRequest
{
    [JsonPropertyName("filter")]
    public StockInfoFilter? Filter { get; set; }

    [JsonPropertyName("cursor")]
    public string Cursor { get; set; } = "";

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 100;
}

public class StockInfoFilter
{
    [JsonPropertyName("offer_id")]
    public List<string>? OfferId { get; set; }

    [JsonPropertyName("product_id")]
    public List<string>? ProductId { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "ALL";
}

/// <summary>
/// Request for /v2/products/stocks
/// </summary>
public class StockUpdateRequest
{
    [JsonPropertyName("stocks")]
    public List<StockUpdateItem> Stocks { get; set; } = new();
}

public class StockUpdateItem
{
    [JsonPropertyName("offer_id")]
    public string? OfferId { get; set; }

    [JsonPropertyName("product_id")]
    public long? ProductId { get; set; }

    [JsonPropertyName("stock")]
    public long Stock { get; set; }

    [JsonPropertyName("warehouse_id")]
    public long WarehouseId { get; set; }
}

/// <summary>
/// Request for /v2/warehouse/list
/// </summary>
public class WarehouseListRequest
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 200;
}

#endregion

#region Response Models

/// <summary>
/// Response from /v4/product/info/stocks
/// </summary>
public class StockInfoResponse
{
    [JsonPropertyName("items")]
    public List<StockInfoItem> Items { get; set; } = new();

    [JsonPropertyName("cursor")]
    public string Cursor { get; set; } = "";

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class StockInfoItem
{
    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("stocks")]
    public List<StockByType> Stocks { get; set; } = new();
}

public class StockByType
{
    [JsonPropertyName("present")]
    public int Present { get; set; }

    [JsonPropertyName("reserved")]
    public int Reserved { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // fbo, fbs

    [JsonPropertyName("warehouse_name")]
    public string WarehouseName { get; set; } = "";

    [JsonPropertyName("shipment_type")]
    public string ShipmentType { get; set; } = "";

    [JsonPropertyName("sku")]
    public long Sku { get; set; }
}

/// <summary>
/// Response from /v2/products/stocks
/// </summary>
public class StockUpdateResponse
{
    [JsonPropertyName("result")]
    public List<StockUpdateResultItem> Result { get; set; } = new();
}

public class StockUpdateResultItem
{
    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("offer_id")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("warehouse_id")]
    public long WarehouseId { get; set; }

    [JsonPropertyName("updated")]
    public bool Updated { get; set; }

    [JsonPropertyName("errors")]
    public List<StockUpdateError>? Errors { get; set; }
}

public class StockUpdateError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// Response from /v2/warehouse/list
/// </summary>
public class WarehouseListResponse
{
    [JsonPropertyName("result")]
    public List<WarehouseInfo> Result { get; set; } = new();
}

public class WarehouseInfo
{
    [JsonPropertyName("warehouse_id")]
    public long WarehouseId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("is_rfbs")]
    public bool IsRfbs { get; set; }

    [JsonPropertyName("is_able_to_set_price")]
    public bool IsAbleToSetPrice { get; set; }

    [JsonPropertyName("has_entrusted_acceptance")]
    public bool HasEntrustedAcceptance { get; set; }

    [JsonPropertyName("first_mile_type")]
    public FirstMileType? FirstMileType { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = ""; // created, disabled, working

    [JsonPropertyName("is_kgt")]
    public bool IsKgt { get; set; }

    [JsonPropertyName("can_print_act_in_advance")]
    public bool CanPrintActInAdvance { get; set; }

    [JsonPropertyName("min_working_days")]
    public int MinWorkingDays { get; set; }

    [JsonPropertyName("is_karantin")]
    public bool IsKarantin { get; set; }

    [JsonPropertyName("has_postings_limit")]
    public bool HasPostingsLimit { get; set; }

    [JsonPropertyName("postings_limit")]
    public int PostingsLimit { get; set; }
}

public class FirstMileType
{
    [JsonPropertyName("dropoff_point_id")]
    public string DropoffPointId { get; set; } = "";

    [JsonPropertyName("dropoff_timeslot_id")]
    public long DropoffTimeslotId { get; set; }

    [JsonPropertyName("first_mile_is_changing")]
    public bool FirstMileIsChanging { get; set; }

    [JsonPropertyName("first_mile_type")]
    public string Type { get; set; } = ""; // DropOff, PickUp
}

#endregion
