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

        var mappingPairs = new HashSet<(int categoryId, int connectionId)> { (5, 3) };

        ProductAdapterLinkMappingEnrichment.Apply(
            dtos,
            mappingPairs,
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
            mappingPairs: [],
            productById: new Dictionary<int, Product>());

        var link = Assert.Single(dtos[0].adapterLinks!);
        Assert.False(link.hasMapping);
        Assert.True(link.mappingMissing);
    }
}
