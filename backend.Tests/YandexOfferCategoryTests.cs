using System.Text.Json;
using Delegat.Marketplace.YandexMarket;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Tests;

public class YandexOfferCategoryTests
{
    [Fact]
    public void GetMappingDto_ReadsMarketCategoryFromPartnerApi()
    {
        const string json = """
            {
              "marketSku": 42,
              "marketSkuName": "Hat",
              "marketCategoryId": 79461128,
              "marketCategoryName": "Головные уборы"
            }
            """;

        var mapping = JsonSerializer.Deserialize<GetMappingDto>(json, YandexRequestChunking.JsonOptions);

        Assert.NotNull(mapping);
        Assert.Equal(79461128, mapping!.MarketCategoryId);
        Assert.Equal("Головные уборы", mapping.MarketCategoryName);
    }

    [Fact]
    public void Id_UsesMappingWhenOfferOmitsCategory()
    {
        var offer = new GetOfferDto { OfferId = "GE57" };
        var mapping = new GetMappingDto
        {
            MarketCategoryId = 79461128,
            MarketCategoryName = "Головные уборы",
        };

        Assert.Equal("79461128", YandexOfferCategory.Id(offer, mapping));
        Assert.Equal("Головные уборы", YandexOfferCategory.Name(offer, mapping));
    }

    [Fact]
    public void Id_IgnoresZeroPlaceholder()
    {
        var offer = new GetOfferDto { OfferId = "GE57", MarketCategoryId = 0 };
        var mapping = new GetMappingDto { MarketCategoryId = 0 };

        Assert.Null(YandexOfferCategory.Id(offer, mapping));
    }

    [Fact]
    public void Id_PrefersOfferCategoryOverMapping()
    {
        var offer = new GetOfferDto { OfferId = "GE57", MarketCategoryId = 111 };
        var mapping = new GetMappingDto { MarketCategoryId = 222 };

        Assert.Equal("111", YandexOfferCategory.Id(offer, mapping));
    }
}
