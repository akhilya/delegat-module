using System.Text.Json.Serialization;

namespace Delegat.Marketplace.YandexMarket.Models;

public class GetOfferPricesRequest
{
    [JsonPropertyName("offerIds")]
    public List<string>? OfferIds { get; set; }
}

public class OfferPricesResultDto
{
    [JsonPropertyName("offers")]
    public List<OfferPriceDto>? Offers { get; set; }
}

public class OfferPriceDto
{
    [JsonPropertyName("offerId")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("price")]
    public PriceWithDiscountDto? Price { get; set; }
}

public class UpdateBusinessPricesRequest
{
    [JsonPropertyName("offers")]
    public List<UpdateBusinessPriceDto> Offers { get; set; } = new();
}

public class UpdateBusinessPriceDto
{
    [JsonPropertyName("offerId")]
    public string OfferId { get; set; } = "";

    [JsonPropertyName("price")]
    public PriceWithDiscountDto Price { get; set; } = new();
}