using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services.Marketplace;

namespace backend.Tests;

public class MarketplaceImportBarcodeTests
{
    // Check digits match ProductController.IsValidEanBarcode (left-to-right EAN weights).
    private const string ValidEan13 = "4600000000008";
    private const string ValidEan8 = "40123459";

    [Fact]
    public void Resolve_UsesProductLevelBarcode()
    {
        var external = new ExternalProduct("1", "SKU", "Name", Barcode: ValidEan13);
        Assert.Equal(ValidEan13, MarketplaceImportBarcode.Resolve(external));
    }

    [Fact]
    public void Resolve_FallsBackToVariantBarcode()
    {
        var external = new ExternalProduct(
            "1",
            "SKU",
            "Name",
            Barcode: null,
            Variants:
            [
                new ExternalProductVariant("v1", Barcodes: ["not-ean", ValidEan8]),
            ]);

        Assert.Equal(ValidEan8, MarketplaceImportBarcode.Resolve(external));
    }

    [Fact]
    public void Resolve_SkipsInvalidAndOversized()
    {
        var external = new ExternalProduct(
            "1",
            "SKU",
            "Name",
            Barcode: "123",
            Variants:
            [
                new ExternalProductVariant("v1", Barcodes: ["460123456789012345"]),
            ]);

        Assert.Null(MarketplaceImportBarcode.Resolve(external));
    }

    [Fact]
    public void WouldChange_TrueWhenLocalEmptyAndExternalValid()
    {
        var product = new Product { barcode = null };
        var external = new ExternalProduct("1", "SKU", "Name", Barcode: ValidEan13);

        Assert.True(MarketplaceImportBarcode.WouldChange(product, external, out var oldValue, out var newValue));
        Assert.Null(oldValue);
        Assert.Equal(ValidEan13, newValue);
    }

    [Fact]
    public void WouldChange_FalseWhenSame()
    {
        var product = new Product { barcode = ValidEan13 };
        var external = new ExternalProduct("1", "SKU", "Name", Barcode: ValidEan13);

        Assert.False(MarketplaceImportBarcode.WouldChange(product, external, out _, out _));
    }
}
