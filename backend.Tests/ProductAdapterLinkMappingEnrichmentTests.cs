using Delegat.Infrastructure;
using Delegat.Models.Products;

namespace Delegat.Tests;

public class ProductAdapterLinkMappingEnrichmentTests
{
    [Fact]
    public void Apply_WritesEnrichedFlagsBackIntoStructList()
    {
        var dtos = new List<Product.ProductReviewDTO>
        {
            new()
            {
                id = 10,
                categoryId = 5,
                adapterLinks =
                [
                    new Product.AdapterLinkSummary
                    {
                        linkId = 100,
                        connectionId = 3,
                        adapterKey = "ozon",
                        status = "Synced",
                        hasMapping = false,
                        mappingMissing = false,
                    },
                ],
            },
        };

        var mappingCounts = new Dictionary<(int categoryId, int connectionId), int>
        {
            [(5, 3)] = 1,
        };

        ProductAdapterLinkMappingEnrichment.Apply(
            dtos,
            mappingCounts,
            productById: new Dictionary<int, Product>());

        var link = Assert.Single(dtos[0].adapterLinks!);
        Assert.True(link.hasMapping);
        Assert.False(link.mappingMissing);
    }

    [Fact]
    public void Apply_MarksMappingMissingWhenNoPair()
    {
        var dtos = new List<Product.ProductReviewDTO>
        {
            new()
            {
                id = 11,
                categoryId = 5,
                adapterLinks =
                [
                    new Product.AdapterLinkSummary
                    {
                        linkId = 101,
                        connectionId = 3,
                        adapterKey = "ozon",
                        status = "Synced",
                        hasMapping = false,
                        mappingMissing = false,
                    },
                ],
            },
        };

        ProductAdapterLinkMappingEnrichment.Apply(
            dtos,
            mappingCounts: new Dictionary<(int categoryId, int connectionId), int>(),
            productById: new Dictionary<int, Product>());

        var link = Assert.Single(dtos[0].adapterLinks!);
        Assert.False(link.hasMapping);
        Assert.True(link.mappingMissing);
        Assert.False(link.mappingUnselected);
    }

    [Fact]
    public void Apply_MarksUnselectedWhenSeveralMappingsAndNoneMatchTheCategory()
    {
        var dtos = new List<Product.ProductReviewDTO>
        {
            new()
            {
                id = 12,
                categoryId = 5,
                adapterLinks =
                [
                    new Product.AdapterLinkSummary
                    {
                        linkId = 102,
                        connectionId = 3,
                        adapterKey = "wildberries",
                        status = "Synced",
                    },
                ],
            },
        };

        ProductAdapterLinkMappingEnrichment.Apply(
            dtos,
            mappingCounts: new Dictionary<(int categoryId, int connectionId), int>
            {
                [(5, 3)] = 2,
            },
            productById: new Dictionary<int, Product>());

        var link = Assert.Single(dtos[0].adapterLinks!);
        Assert.True(link.hasMapping);
        Assert.True(link.mappingUnselected);
        Assert.False(link.mappingMissing);
    }
}
