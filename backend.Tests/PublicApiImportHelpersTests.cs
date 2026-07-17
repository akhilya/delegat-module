using Delegat.Controllers.PublicApi;

namespace Delegat.Tests;

public class PublicApiImportHelpersTests
{
    private static readonly IReadOnlyDictionary<string, int> Mappings =
        new Dictionary<string, int> { ["ext-cat-1"] = 100 };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_BlankExternalId_Skips(string? externalId)
    {
        var ok = PublicApiImportHelpers.TryResolveImportItem(
            externalId, "ext-cat-1", null, false, false, Mappings, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryResolve_ExplicitTargetCategory_Wins()
    {
        var ok = PublicApiImportHelpers.TryResolveImportItem(
            "sku-1", "ext-cat-1", 55, false, false, Mappings, out var categoryId);
        Assert.True(ok);
        Assert.Equal(55, categoryId);
    }

    [Fact]
    public void TryResolve_MappedExternalCategory_Resolves()
    {
        var ok = PublicApiImportHelpers.TryResolveImportItem(
            "sku-1", "ext-cat-1", null, false, false, Mappings, out var categoryId);
        Assert.True(ok);
        Assert.Equal(100, categoryId);
    }

    [Fact]
    public void TryResolve_UnmappedWithoutForce_Skips()
    {
        var ok = PublicApiImportHelpers.TryResolveImportItem(
            "sku-1", "unknown-cat", null, false, false, Mappings, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryResolve_UnmappedWithItemForce_QueuesWithoutCategory()
    {
        var ok = PublicApiImportHelpers.TryResolveImportItem(
            "sku-1", "unknown-cat", null, true, false, Mappings, out var categoryId);
        Assert.True(ok);
        Assert.Null(categoryId);
    }

    [Fact]
    public void TryResolve_UnmappedWithBodyForce_QueuesWithoutCategory()
    {
        var ok = PublicApiImportHelpers.TryResolveImportItem(
            "sku-1", "unknown-cat", null, false, true, Mappings, out var categoryId);
        Assert.True(ok);
        Assert.Null(categoryId);
    }

    [Fact]
    public void TryResolve_NoExternalCategory_QueuesWithNullCategory()
    {
        var ok = PublicApiImportHelpers.TryResolveImportItem(
            "sku-1", null, null, false, false, Mappings, out var categoryId);
        Assert.True(ok);
        Assert.Null(categoryId);
    }
}
