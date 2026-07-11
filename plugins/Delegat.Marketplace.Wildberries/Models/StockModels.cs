using System.Text.Json.Serialization;

namespace Delegat.Marketplace.Wildberries.Models;

#region Warehouses

public class WbWarehouse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("officeId")]
    public long OfficeId { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("cargoType")]
    public int CargoType { get; set; }

    [JsonPropertyName("deliveryType")]
    public int DeliveryType { get; set; }
}

#endregion

#region Update Stocks

public class WbUpdateStocksRequest
{
    [JsonPropertyName("stocks")]
    public List<WbStockItem> Stocks { get; set; } = new();
}

public class WbStockItem
{
    [JsonPropertyName("chrtId")]
    public int ChrtId { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

#endregion

#region Stock Levels

public class WbStockLevelsResponse
{
    [JsonPropertyName("stocks")]
    public List<WbStockLevel>? Stocks { get; set; }
}

public class WbStockLevel
{
    [JsonPropertyName("chrtId")]
    public int ChrtId { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

#endregion
