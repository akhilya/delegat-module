using System.Text.Json.Serialization;

namespace Delegat.Marketplace.YandexMarket.Models;

public class GetStocksRequest
{
    [JsonPropertyName("offerIds")]
    public List<string>? OfferIds { get; set; }
}

public class StocksResultDto
{
    [JsonPropertyName("warehouses")]
    public List<WarehouseStockDto>? Warehouses { get; set; }
}

public class WarehouseStockDto
{
    [JsonPropertyName("warehouseId")]
    public long? WarehouseId { get; set; }

    [JsonPropertyName("offers")]
    public List<WarehouseOfferStockDto>? Offers { get; set; }
}

public class WarehouseOfferStockDto
{
    [JsonPropertyName("offerId")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("stocks")]
    public List<StockItemDto>? Stocks { get; set; }
}

public class StockItemDto
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class UpdateStocksRequest
{
    [JsonPropertyName("skus")]
    public List<UpdateStockDto> Skus { get; set; } = new();
}

public class UpdateStockDto
{
    [JsonPropertyName("sku")]
    public string Sku { get; set; } = "";

    [JsonPropertyName("items")]
    public List<UpdateStockItemDto> Items { get; set; } = new();
}

public class UpdateStockItemDto
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }
}