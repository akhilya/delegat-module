using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.Ozon.Models;

namespace Delegat.Tests;

public class OzonMapToExternalProductTests
{
    [Fact]
    public void MapToExternalProduct_IncludesCategoryPriceAttributesAndImages()
    {
        var item = new ProductInfoItem
        {
            Id = 1001,
            OfferId = "SKU-1",
            Name = "Test product",
            Price = "199.50",
            DescriptionCategoryId = 17028922,
            TypeId = 97083481,
            Barcode = "4601234567890",
            PrimaryImage = ["https://cdn.example/primary.jpg"],
            Images = ["https://cdn.example/primary.jpg", "https://cdn.example/2.jpg"],
            Stocks = new ProductStocks { Present = 7 },
        };

        var attrs = new ProductAttributesItem
        {
            Attributes =
            [
                new ProductAttributeInfo
                {
                    AttributeId = 85,
                    Values = [new AttributeValueInfo { Value = "Acme" }],
                },
                new ProductAttributeInfo
                {
                    AttributeId = 8229,
                    Values = [new AttributeValueInfo { Value = "Blue" }],
                },
            ],
        };

        var categoryCache = new Dictionary<string, string>
        {
            ["17028922:97083481"] = "Apparel / T-shirts",
        };

        var product = OzonPlugin.MapToExternalProduct(item, attrs, categoryCache);

        Assert.Equal("17028922:97083481", product.CategoryId);
        Assert.Equal("Apparel / T-shirts", product.CategoryName);
        Assert.Equal(199.50m, product.Price);
        Assert.Equal(7, product.Quantity);
        Assert.Equal("Acme", product.Brand);
        Assert.Equal("4601234567890", product.Barcode);
        Assert.NotNull(product.Attributes);
        Assert.Equal("Blue", product.Attributes!["8229"]);
        Assert.Equal(2, product.ImageUrls?.Count);
        Assert.Equal("https://cdn.example/primary.jpg", product.ImageUrls![0]);
    }

    [Fact]
    public void MapToExternalProduct_UsesLegacyCategoryIdWhenDescriptionMissing()
    {
        var item = new ProductInfoItem
        {
            Id = 2,
            OfferId = "SKU-2",
            Name = "Legacy",
            CategoryId = 12345,
            TypeId = 99,
        };

        var product = OzonPlugin.MapToExternalProduct(item, attrs: null);

        Assert.Equal("12345:99", product.CategoryId);
        Assert.Null(product.CategoryName);
    }
}
