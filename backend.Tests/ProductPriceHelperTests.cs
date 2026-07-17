using Delegat.Infrastructure;

namespace Delegat.Tests;

public class ProductPriceHelperTests
{
    [Theory]
    [InlineData(1000, 800, 20)]
    [InlineData(100, 50, 50)]
    [InlineData(199, 100, 50)]
    public void ComputeDiscountPercent_ValidPrices_ReturnsRoundedPercent(
        decimal oldPrice, decimal sellingPrice, int expected)
    {
        Assert.Equal(expected, ProductPriceHelper.ComputeDiscountPercent(oldPrice, sellingPrice));
    }

    [Fact]
    public void ComputeDiscountPercent_ClampsTo99()
    {
        Assert.Equal(99, ProductPriceHelper.ComputeDiscountPercent(1000m, 1m));
    }

    [Fact]
    public void ComputeDiscountPercent_SellingAboveOld_ClampsTo0()
    {
        Assert.Equal(0, ProductPriceHelper.ComputeDiscountPercent(100m, 150m));
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(100, null)]
    [InlineData(0, 50)]
    [InlineData(-10, 50)]
    public void ComputeDiscountPercent_InvalidInputs_ReturnsNull(int? oldPrice, int? sellingPrice)
    {
        Assert.Null(ProductPriceHelper.ComputeDiscountPercent(oldPrice, sellingPrice));
    }

    [Fact]
    public void SanitizeOldPrice_KeepsRealDiscount()
    {
        Assert.Equal(1200m, ProductPriceHelper.SanitizeOldPrice(1000m, 1200m));
    }

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(1000, 900)]
    public void SanitizeOldPrice_DropsEqualOrLowerOldPrice(decimal price, decimal oldPrice)
    {
        Assert.Null(ProductPriceHelper.SanitizeOldPrice(price, oldPrice));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void SanitizeOldPrice_NullOrNonPositiveOld_ReturnsNull(int? oldPrice)
    {
        Assert.Null(ProductPriceHelper.SanitizeOldPrice(1000m, oldPrice));
    }

    [Fact]
    public void SanitizeOldPrice_NullPrice_KeepsPositiveOld()
    {
        Assert.Equal(1200m, ProductPriceHelper.SanitizeOldPrice(null, 1200m));
    }
}
