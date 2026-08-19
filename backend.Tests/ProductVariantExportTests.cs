using Delegat.Models.Products;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class ProductVariantExportTests
{
    [Fact]
    public void ToPayload_IncludesLocalIdAndSku_EvenWithoutMarketplaceLink()
    {
        var variant = new ProductVariant
        {
            Id = 42,
            sku = "TEE-M",
            size = "M",
            barcode = "2000000000012",
            price = 990m,
            quantity = 3
        };

        var payload = ProductVariantExport.ToPayload(variant, link: null);

        Assert.Equal(42, payload["id"]);
        Assert.Equal("TEE-M", payload["sku"]);
        Assert.Equal("M", payload["techSize"]);
        Assert.Null(payload["variantId"]);
        Assert.Equal("2000000000012", payload["barcode"]);
        Assert.Equal(990m, payload["price"]);
        Assert.Equal(3, payload["quantity"]);
    }

    [Fact]
    public void ToPayload_UsesMarketplaceLinkIds()
    {
        var variant = new ProductVariant { Id = 7, size = "42" };
        var link = new ProductVariantLink
        {
            externalVariantId = "chrt-9",
            marketplaceSize = "42"
        };

        var payload = ProductVariantExport.ToPayload(variant, link);

        Assert.Equal("chrt-9", payload["variantId"]);
        Assert.Equal("42", payload["marketplaceSize"]);
    }

    [Fact]
    public void ReadExportedVariantIds_ParsesStringMap()
    {
        var details = new Dictionary<string, object>
        {
            ["variantIds"] = new Dictionary<string, string>
            {
                ["12"] = "101",
                ["0"] = "skip",
                ["bad"] = "nope"
            }
        };

        var ids = ProductVariantExport.ReadExportedVariantIds(details);

        Assert.Equal(new Dictionary<int, string> { [12] = "101" }, ids);
    }
}
