using Delegat.Infrastructure;

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
        var specs = ProductSortHelper.ParseSortSpecs(null, "name", false);

        Assert.Single(specs);
        Assert.Equal("name", specs[0].Column);
        Assert.False(specs[0].Desc);
    }
}