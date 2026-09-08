using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class ProductFileMatcherTests
{
    [Fact]
    public void Matches_SkuInFileName()
    {
        var products = new[]
        {
            new ProductFileMatchProduct(12, "SKU-HELLO", "4601234567890", "Hello soap"),
            new ProductFileMatchProduct(13, "OTHER", null, "Other"),
        };
        var files = new[]
        {
            new ProductFileMatchInput("photos/SKU-HELLO.jpg", "SKU-HELLO.jpg"),
            new ProductFileMatchInput("readme.txt", "readme.txt"),
        };

        var result = ProductFileMatcher.Match(files, products);

        var hit = Assert.Single(result, r => r.RelativePath == "photos/SKU-HELLO.jpg");
        Assert.Equal(12, hit.ProductId);
        Assert.Equal("sku", hit.Reason);
        Assert.True(hit.Confidence >= 0.9);
        Assert.DoesNotContain(result, r => r.RelativePath == "readme.txt" && r.ProductId != null);
    }

    [Fact]
    public void Matches_BarcodeThenNameStem()
    {
        var products = new[]
        {
            new ProductFileMatchProduct(1, "A", "460111", "Blue Mug"),
        };
        var files = new[]
        {
            new ProductFileMatchInput("460111.png", "460111.png"),
            new ProductFileMatchInput("Blue Mug.JPG", "Blue Mug.JPG"),
        };

        var result = ProductFileMatcher.Match(files, products);

        Assert.Equal(1, result[0].ProductId);
        Assert.Equal("barcode", result[0].Reason);
        Assert.Equal(1, result[1].ProductId);
        Assert.Equal("name", result[1].Reason);
    }
}
