using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.YandexMarket;

namespace Delegat.Tests;

public class PluginCapabilitySmokeTests
{
    [Fact]
    public void YandexPlugin_ExposesIdentityCapabilitiesAndRateLimitConfiguration()
    {
        var plugin = new YandexMarketPlugin();

        Assert.Equal("yandex", plugin.Key);
        Assert.False(string.IsNullOrWhiteSpace(plugin.DisplayName));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.GetCategories));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.SearchPublicCatalog));
        AssertCommerceFlags(plugin.Capabilities);

        var rateLimit = plugin.GetRateLimitConfig();
        Assert.NotNull(rateLimit);
        Assert.True(rateLimit!.MaxRequestsPerSecond > 0);
    }

    [Fact]
    public void OzonAndWildberries_ExposeLiveCommerceCapabilities()
    {
        AssertCommerceFlags(new OzonPlugin().Capabilities);
        AssertCommerceFlags(new WildberriesPlugin().Capabilities);
        Assert.True(new OzonPlugin().Capabilities.HasFlag(MarketplaceCapabilities.SearchPublicCatalog));
        Assert.True(new WildberriesPlugin().Capabilities.HasFlag(MarketplaceCapabilities.SearchPublicCatalog));
        Assert.True(new OzonPlugin().Capabilities.HasFlag(MarketplaceCapabilities.GetFulfillmentLocations));
        Assert.False(new OzonPlugin().Capabilities.HasFlag(MarketplaceCapabilities.ManageShipments));
        Assert.True(new WildberriesPlugin().Capabilities.HasFlag(MarketplaceCapabilities.GetFulfillmentLocations));
        Assert.True(new WildberriesPlugin().Capabilities.HasFlag(MarketplaceCapabilities.ManageShipments));
    }

    private static void AssertCommerceFlags(MarketplaceCapabilities capabilities)
    {
        Assert.True(capabilities.HasFlag(MarketplaceCapabilities.ImportPrices));
        Assert.True(capabilities.HasFlag(MarketplaceCapabilities.GetOrders));
        Assert.True(capabilities.HasFlag(MarketplaceCapabilities.UpdateOrderStatus));
        Assert.True(capabilities.HasFlag(MarketplaceCapabilities.GetOrderLabels));
        Assert.True(capabilities.HasFlag(MarketplaceCapabilities.GetPromotions));
        Assert.True(capabilities.HasFlag(MarketplaceCapabilities.UpdatePromotions));
    }
}
