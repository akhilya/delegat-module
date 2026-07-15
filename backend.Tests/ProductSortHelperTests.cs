using Delegat.Infrastructure;
using Delegat.Models.Products;

namespace Delegat.Tests;

public class ProductSortHelperTests
{
    [Fact]
    public void ParseSortSpecs_PipeSeparator_ParsesTwoColumns()
    {
        var specs = ProductSortHelper.ParseSortSpecs("price:desc|storageQuantity:asc", null, true);

        Assert.Equal(2, specs.Count);
        Assert.Equal("price", specs[0].Column);
        Assert.True(specs[0].Desc);
        Assert.Equal("storagequantity", specs[1].Column);
        Assert.False(specs[1].Desc);
    }

    [Fact]
    public void ParseSortSpecs_RepeatedQueryParams_ParsesTwoColumns()
    {
        var specs = ProductSortHelper.ParseSortSpecs(
            new[] { "price:desc", "storageQuantity:asc" },
            null,
            true);

        Assert.Equal(2, specs.Count);
        Assert.Equal("price", specs[0].Column);
        Assert.Equal("storagequantity", specs[1].Column);
        Assert.False(specs[1].Desc);
    }

    [Fact]
    public void ParseSortSpecs_PipeSeparator_SecondColumnDirectionDiffers()
    {
        var asc = ProductSortHelper.ParseSortSpecs("price:desc|storageQuantity:asc", null, true);
        var desc = ProductSortHelper.ParseSortSpecs("price:desc|storageQuantity:desc", null, true);

        Assert.Equal(asc[0], desc[0]);
        Assert.NotEqual(asc[1].Desc, desc[1].Desc);
    }

    [Fact]
    public void ParseSortSpecs_CommaSeparator_ParsesTwoColumns()
    {
        var specs = ProductSortHelper.ParseSortSpecs("price:desc,storageQuantity:asc", null, true);

        Assert.Equal(2, specs.Count);
        Assert.Equal("price", specs[0].Column);
        Assert.Equal("storagequantity", specs[1].Column);
        Assert.False(specs[1].Desc);
    }

    [Fact]
    public void ParseSortSpecs_LegacySortBy_Fallback()
    {
        var specs = ProductSortHelper.ParseSortSpecs((string?)null, "name", false);

        Assert.Single(specs);
        Assert.Equal("name", specs[0].Column);
        Assert.False(specs[0].Desc);
    }

    [Fact]
    public void BuildOrderBy_SecondColumnDirection_ChangesOrderForTiedPrimaryValues()
    {
        var products = new[]
        {
            CreateProduct(1, 100m, 10),
            CreateProduct(2, 100m, 5),
            CreateProduct(3, 100m, 20),
        }.AsQueryable();

        var ascSpecs = ProductSortHelper.ParseSortSpecs("price:desc|storageQuantity:asc", null, true);
        var descSpecs = ProductSortHelper.ParseSortSpecs("price:desc|storageQuantity:desc", null, true);

        var ascOrder = ProductSortHelper.BuildOrderBy(ascSpecs, hasSearch: false, searchQuery: null)(products)
            .Select(p => p.Id)
            .ToArray();
        var descOrder = ProductSortHelper.BuildOrderBy(descSpecs, hasSearch: false, searchQuery: null)(products)
            .Select(p => p.Id)
            .ToArray();

        Assert.Equal(new[] { 2, 1, 3 }, ascOrder);
        Assert.Equal(new[] { 3, 1, 2 }, descOrder);
        Assert.NotEqual(ascOrder, descOrder);
    }

    private static Product CreateProduct(int id, decimal price, int storageQuantity) =>
        new()
        {
            Id = id,
            sku = $"SKU{id}",
            name = $"Product {id}",
            description = "",
            price = price,
            storageQuantity = storageQuantity,
            storeId = 1,
        };
}