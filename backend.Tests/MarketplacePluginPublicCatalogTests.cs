using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Tests;

public class MarketplacePluginPublicCatalogTests
{
    [Fact]
    public async Task SearchPublicCatalog_Default_ThrowsNotSupported()
    {
        var plugin = new StubPlugin();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            plugin.SearchPublicCatalogAsync(new ConnectionContext(), "nike"));
    }

    [Fact]
    public async Task GetPublicProduct_Default_ThrowsNotSupported()
    {
        var plugin = new StubPlugin();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            plugin.GetPublicProductAsync(new ConnectionContext(), "1"));
    }

    [Fact]
    public void SearchPublicCatalog_Flag_IsDistinctFromImportProduct()
    {
        var flag = MarketplaceCapabilities.SearchPublicCatalog;
        Assert.Equal(1 << 21, (int)flag);
        Assert.False(flag.HasFlag(MarketplaceCapabilities.ImportProduct));
        Assert.False(MarketplaceCapabilities.FullProducts.HasFlag(flag));
    }

    private sealed class StubPlugin : MarketplacePluginBase
    {
        public override string Key => "stub";
        public override string DisplayName => "Stub";
        public override string Version => "0";
        public override MarketplaceCapabilities Capabilities => MarketplaceCapabilities.None;

        public override CredentialsSchema GetCredentialsSchema() => new();

        public override Task<ConnectionTestResult> TestConnectionAsync(
            ConnectionContext context, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true));
    }
}
