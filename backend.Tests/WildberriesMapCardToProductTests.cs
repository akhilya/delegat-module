using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.Wildberries.Models;

namespace Delegat.Tests;

public class WildberriesMapCardToProductTests
{
    [Fact]
    public void MapCardToProduct_PutsDimensionsAndBrandOnAttributes()
    {
        var card = new WbCard
        {
            NmId = 11,
            VendorCode = "ART-1",
            Title = "Tee",
            Brand = "Acme",
            SubjectId = 42,
            Dimensions = new WbDimensions
            {
                Length = 20,
                Width = 10,
                Height = 5,
                WeightBrutto = 0.15,
            },
        };

        var product = WildberriesPlugin.MapCardToProduct(card);

        Assert.Equal("Acme", product.Brand);
        Assert.NotNull(product.Attributes);
        Assert.Equal(20, product.Attributes!["length"]);
        Assert.Equal(10, product.Attributes["width"]);
        Assert.Equal(5, product.Attributes["height"]);
        Assert.Equal(0.15, Assert.IsType<double>(product.Attributes["weight"]));
        Assert.Equal("Acme", product.Attributes["brand"]);
        Assert.Null(product.Price);
        Assert.Equal("42", product.CategoryId);
    }

    [Fact]
    public void MapCardToProduct_OmitsCategoryWhenSubjectIdIsZero()
    {
        var card = new WbCard
        {
            NmId = 11,
            VendorCode = "ART-1",
            Title = "Tee",
            SubjectId = 0,
        };

        var product = WildberriesPlugin.MapCardToProduct(card);

        Assert.Null(product.CategoryId);
        Assert.Null(product.CategoryName);
    }
}
