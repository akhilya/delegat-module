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

    [Fact]
    public void GetPromotions_Flag_IsDistinctFromGetOrders()
    {
        Assert.Equal(1 << 22, (int)MarketplaceCapabilities.GetPromotions);
        Assert.Equal(1 << 23, (int)MarketplaceCapabilities.UpdatePromotions);
        Assert.False(MarketplaceCapabilities.GetPromotions.HasFlag(MarketplaceCapabilities.GetOrders));
        Assert.False(MarketplaceCapabilities.FullProducts.HasFlag(MarketplaceCapabilities.GetPromotions));
    }

    [Fact]
    public void GetOrderLabels_Flag_IsDistinctFromGetOrders()
    {
        Assert.Equal(1 << 24, (int)MarketplaceCapabilities.GetOrderLabels);
        Assert.False(MarketplaceCapabilities.GetOrderLabels.HasFlag(MarketplaceCapabilities.GetOrders));
        Assert.False(MarketplaceCapabilities.FullProducts.HasFlag(MarketplaceCapabilities.GetOrderLabels));
    }

    [Fact]
    public void FulfillmentFlags_AreDistinctFromOrders()
    {
        Assert.Equal(1 << 25, (int)MarketplaceCapabilities.GetFulfillmentLocations);
        Assert.Equal(1 << 26, (int)MarketplaceCapabilities.ManageShipments);
        Assert.False(MarketplaceCapabilities.GetFulfillmentLocations.HasFlag(MarketplaceCapabilities.GetOrders));
        Assert.False(MarketplaceCapabilities.ManageShipments.HasFlag(MarketplaceCapabilities.GetOrderLabels));
        Assert.False(MarketplaceCapabilities.FullProducts.HasFlag(MarketplaceCapabilities.ManageShipments));
    }

    [Fact]
    public async Task GetOrderLabel_Default_ThrowsNotSupported()
    {
        var plugin = new StubPlugin();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            plugin.GetOrderLabelAsync(new ConnectionContext(), "1"));
    }

    [Fact]
    public async Task GetFulfillmentLocations_Default_ThrowsNotSupported()
    {
        var plugin = new StubPlugin();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            plugin.GetFulfillmentLocationsAsync(new ConnectionContext()));
    }

    [Fact]
    public async Task GetShipments_Default_ThrowsNotSupported()
    {
        var plugin = new StubPlugin();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            plugin.GetShipmentsAsync(new ConnectionContext()));
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
