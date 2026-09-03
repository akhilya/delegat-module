using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class PublicCatalogQueryTests
{
    [Fact]
    public void Parse_WildberriesDetailUrl_IsProductId()
    {
        var parsed = PublicCatalogQuery.Parse(
            "https://www.wildberries.ru/catalog/12345678/detail.aspx?targetUrl=GP");

        Assert.Equal(PublicCatalogQueryKind.ProductIds, parsed.Kind);
        Assert.Equal(["12345678"], parsed.ProductIds);
    }

    [Fact]
    public void Parse_DigitsOnly_IsProductId()
    {
        var parsed = PublicCatalogQuery.Parse("  99001122  ");

        Assert.Equal(PublicCatalogQueryKind.ProductIds, parsed.Kind);
        Assert.Equal(["99001122"], parsed.ProductIds);
    }

    [Fact]
    public void Parse_Text_IsStorefrontSearch()
    {
        var parsed = PublicCatalogQuery.Parse("nike air max");

        Assert.Equal(PublicCatalogQueryKind.Text, parsed.Kind);
        Assert.Equal("nike air max", parsed.Text);
        Assert.Empty(parsed.ProductIds);
    }

    [Fact]
    public void Parse_WhitespaceListOfIds_IsProductIds()
    {
        var parsed = PublicCatalogQuery.Parse("111\n222  333");

        Assert.Equal(PublicCatalogQueryKind.ProductIds, parsed.Kind);
        Assert.Equal(["111", "222", "333"], parsed.ProductIds);
    }

    [Fact]
    public void Parse_MixedIdAndText_IsTextSearch()
    {
        var parsed = PublicCatalogQuery.Parse("111 nike");

        Assert.Equal(PublicCatalogQueryKind.Text, parsed.Kind);
        Assert.Equal("111 nike", parsed.Text);
    }

    [Fact]
    public void ExtractProductId_NmQueryString()
    {
        Assert.Equal(
            "555",
            PublicCatalogQuery.ExtractProductId("https://www.wildberries.ru/catalog?nm=555"));
    }

    [Fact]
    public void Parse_OzonProductUrl_IsProductId()
    {
        var parsed = PublicCatalogQuery.Parse(
            "https://www.ozon.ru/product/naushniki-apple-airpods-165204773/?at=x");

        Assert.Equal(PublicCatalogQueryKind.ProductIds, parsed.Kind);
        Assert.Equal(["165204773"], parsed.ProductIds);
    }

    [Fact]
    public void Parse_OzonNumericProductPath_IsProductId()
    {
        var parsed = PublicCatalogQuery.Parse("https://ozon.ru/product/165204773/");
        Assert.Equal(["165204773"], parsed.ProductIds);
    }

    [Fact]
    public void Parse_YandexCardUrl_IsProductId()
    {
        var parsed = PublicCatalogQuery.Parse(
            "https://market.yandex.ru/card/apple-iphone-15/12345678?sku=999");

        Assert.Equal(PublicCatalogQueryKind.ProductIds, parsed.Kind);
        Assert.Equal(["999"], parsed.ProductIds);
    }

    [Fact]
    public void Parse_YandexProductSlugUrl_IsProductId()
    {
        var parsed = PublicCatalogQuery.Parse(
            "https://market.yandex.ru/product--apple-iphone-15/12345678");

        Assert.Equal(["12345678"], parsed.ProductIds);
    }

    [Fact]
    public void Parse_MixedMarketplaceUrls_AreProductIds()
    {
        var parsed = PublicCatalogQuery.Parse(
            "https://www.ozon.ru/product/slug-1/\nhttps://www.wildberries.ru/catalog/2/detail.aspx");

        Assert.Equal(["1", "2"], parsed.ProductIds);
    }
}
