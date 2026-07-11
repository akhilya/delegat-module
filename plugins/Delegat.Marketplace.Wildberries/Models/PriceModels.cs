using System.Text.Json.Serialization;

namespace Delegat.Marketplace.Wildberries.Models;

#region Goods List (Prices)

public class WbGoodsListResponse
{
    [JsonPropertyName("data")]
    public WbGoodsListData? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

public class WbGoodsListData
{
    [JsonPropertyName("listGoods")]
    public List<WbGoodsItem>? ListGoods { get; set; }
}

public class WbGoodsItem
{
    [JsonPropertyName("nmID")]
    public int NmId { get; set; }

    [JsonPropertyName("vendorCode")]
    public string? VendorCode { get; set; }

    [JsonPropertyName("sizes")]
    public List<WbGoodsSize>? Sizes { get; set; }

    [JsonPropertyName("currencyIsoCode4217")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("discount")]
    public int Discount { get; set; }

    [JsonPropertyName("editableSizePrice")]
    public bool EditableSizePrice { get; set; }
}

public class WbGoodsSize
{
    [JsonPropertyName("sizeID")]
    public long SizeId { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("discountedPrice")]
    public decimal DiscountedPrice { get; set; }

    [JsonPropertyName("techSizeName")]
    public string? TechSizeName { get; set; }
}

#endregion

#region Set Prices

public class WbSetPricesRequest
{
    [JsonPropertyName("data")]
    public List<WbPriceItem> Data { get; set; } = new();
}

public class WbPriceItem
{
    [JsonPropertyName("nmID")]
    public int NmId { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("discount")]
    public int Discount { get; set; }
}

public class WbSetPricesResponse
{
    [JsonPropertyName("data")]
    public WbSetPricesData? Data { get; set; }

    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

public class WbSetPricesData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("alreadyExists")]
    public bool AlreadyExists { get; set; }
}

#endregion
