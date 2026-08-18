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
}
