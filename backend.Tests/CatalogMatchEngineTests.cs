using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class CatalogMatchEngineTests
{
    private const string ValidEan13 = "4600000000008";
    private const string OtherEan13 = "4600000000015";

    private static CatalogMatchEngine.LocalProduct Local(
        int id,
        string sku,
        string? barcode = null,
        string? name = null)
        => new(id, sku, barcode, name ?? $"Product {id}");

    private static CatalogMatchEngine.RemoteProduct Remote(
        string externalId,
        string? sku,
        string? barcode = null,
        string? name = null)
        => new(externalId, sku, barcode, name ?? externalId);

    [Fact]
    public void Match_BarcodeOneToOne_WinsOverSku()
    {
        var locals = new[]
        {
            Local(1, "SKU-A", ValidEan13),
            Local(2, "OFFER-1", OtherEan13),
        };
        var remotes = new[]
        {
            Remote("wb-9", "OFFER-1", ValidEan13, "Card"),
        };

        var rows = CatalogMatchEngine.Match(locals, remotes);

        var row = Assert.Single(rows);
        Assert.Equal(CatalogMatchStatus.Matched, row.Status);
        Assert.Equal(1, row.ProductId);
        Assert.Equal("barcode", row.MatchedBy);
    }

    [Fact]
    public void Match_SkuIsCaseInsensitiveAndTrimmed()
    {
        var locals = new[] { Local(10, "  Abc-1  ") };
        var remotes = new[] { Remote("oz-1", "abc-1") };

        var rows = CatalogMatchEngine.Match(locals, remotes);

        var row = Assert.Single(rows);
        Assert.Equal(CatalogMatchStatus.Matched, row.Status);
        Assert.Equal(10, row.ProductId);
        Assert.Equal("sku", row.MatchedBy);
    }

    [Fact]
    public void Match_DuplicateLocalSku_IsAmbiguous()
    {
        var locals = new[]
        {
            Local(1, "SHARE"),
            Local(2, "share"),
        };
        var remotes = new[] { Remote("oz-1", "SHARE") };

        var rows = CatalogMatchEngine.Match(locals, remotes);

        var row = Assert.Single(rows);
        Assert.Equal(CatalogMatchStatus.Ambiguous, row.Status);
        Assert.Null(row.ProductId);
        Assert.Equal(2, row.Candidates.Count);
    }

    [Fact]
    public void Match_DuplicateLocalBarcode_IsAmbiguous()
    {
        var locals = new[]
        {
            Local(1, "A", ValidEan13),
            Local(2, "B", ValidEan13),
        };
        var remotes = new[] { Remote("wb-1", "X", ValidEan13) };

        var rows = CatalogMatchEngine.Match(locals, remotes);

        var row = Assert.Single(rows);
        Assert.Equal(CatalogMatchStatus.Ambiguous, row.Status);
        Assert.Equal("barcode", row.MatchedBy);
    }

    [Fact]
    public void Match_NoKeys_IsUnmatched()
    {
        var locals = new[] { Local(1, "LOCAL") };
        var remotes = new[] { Remote("wb-1", "OTHER") };

        var rows = CatalogMatchEngine.Match(locals, remotes);

        var row = Assert.Single(rows);
        Assert.Equal(CatalogMatchStatus.Unmatched, row.Status);
        Assert.Null(row.ProductId);
        Assert.Empty(row.Candidates);
    }

    [Fact]
    public void Match_InvalidBarcodeFallsThroughToSku()
    {
        var locals = new[] { Local(1, "SKU-1", "not-ean") };
        var remotes = new[] { Remote("oz-1", "SKU-1", "also-bad") };

        var rows = CatalogMatchEngine.Match(locals, remotes);

        var row = Assert.Single(rows);
        Assert.Equal(CatalogMatchStatus.Matched, row.Status);
        Assert.Equal("sku", row.MatchedBy);
        Assert.Equal(1, row.ProductId);
    }

    [Fact]
    public void Match_DoesNotUseProductNames()
    {
        var locals = new[] { Local(1, "A", name: "Red shoes") };
        var remotes = new[] { Remote("wb-1", "B", name: "Red shoes") };

        var rows = CatalogMatchEngine.Match(locals, remotes);

        Assert.Equal(CatalogMatchStatus.Unmatched, Assert.Single(rows).Status);
    }
}
