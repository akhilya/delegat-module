using Delegat.Marketplace.YandexMarket;

namespace Delegat.Tests;

public class YandexCatalogPagingTests
{
    [Fact]
    public void PageSizes_ShrinksAFullPageDownToOneOffer()
    {
        Assert.Equal(new[] { 100, 50, 20, 10, 1 }, YandexCatalogPaging.PageSizes(1000));
    }

    [Fact]
    public void PageSizes_DoesNotGrowPastTheRequestedLimit()
    {
        Assert.Equal(new[] { 10, 5, 1 }, YandexCatalogPaging.PageSizes(10));
    }
}
