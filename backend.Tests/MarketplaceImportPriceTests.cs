using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class MarketplaceImportPriceTests
{
    [Fact]
    public void Apply_FillsWhenCardHasNoPrice()
    {
        var product = new ExternalProduct("1", "SKU", "Tee");
        var filled = MarketplaceImportPrice.Apply(
            product,
            new PriceInfo("1", Price: 1490m, DiscountedPrice: 990m));

        Assert.Equal(1490m, filled.Price);
    }

    [Fact]
    public void Apply_UsesDiscountedWhenPriceMissing()
    {
        var product = new ExternalProduct("1", "SKU", "Tee");
        var filled = MarketplaceImportPrice.Apply(
            product,
            new PriceInfo("1", Price: null, DiscountedPrice: 990m));

        Assert.Equal(990m, filled.Price);
    }

    [Fact]
    public void Apply_DoesNotOverwriteExistingPrice()
    {
        var product = new ExternalProduct("1", "SKU", "Tee", Price: 10m);
        var filled = MarketplaceImportPrice.Apply(
            product,
            new PriceInfo("1", Price: 1490m));

        Assert.Equal(10m, filled.Price);
    }

    [Fact]
    public void Apply_TreatsZeroAsMissing()
    {
        var product = new ExternalProduct("1", "SKU", "Tee", Price: 0m);
        var filled = MarketplaceImportPrice.Apply(
            product,
            new PriceInfo("1", Price: 50m));

        Assert.Equal(50m, filled.Price);
    }

    [Fact]
    public void Apply_UsesOldPriceWhenSellingIsZero()
    {
        var product = new ExternalProduct("1", "SKU", "Tee");
        var filled = MarketplaceImportPrice.Apply(
            product,
            new PriceInfo("1", Price: 0m, OldPrice: 1500m));

        Assert.Equal(1500m, filled.Price);
    }

    [Fact]
    public async Task FillMissingAsync_LooksUpOnlyProductsWithoutPrice()
    {
        List<string>? requested = null;
        var products = new List<ExternalProduct>
        {
            new("a", "A", "Has price", Price: 10m),
            new("b", "B", "No price"),
        };

        var filled = await MarketplaceImportPrice.FillMissingAsync(
            products,
            (ids, _) =>
            {
                requested = ids;
                return Task.FromResult(new List<PriceInfo> { new("b", Price: 77m) });
            },
            CancellationToken.None);

        Assert.Equal(new List<string> { "b" }, requested);
        Assert.Equal(10m, filled[0].Price);
        Assert.Equal(77m, filled[1].Price);
    }

    [Fact]
    public async Task FillMissingAsync_KeepsCardWhenPricesApiThrows()
    {
        var products = new List<ExternalProduct> { new("1", "SKU", "Tee") };

        var filled = await MarketplaceImportPrice.FillMissingAsync(
            products,
            (_, _) => throw new InvalidOperationException("prices down"),
            CancellationToken.None);

        Assert.Null(filled[0].Price);
    }
}
