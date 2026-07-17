using Delegat.Marketplace.SDK;
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

        var rateLimit = plugin.GetRateLimitConfig();
        Assert.NotNull(rateLimit);
        Assert.True(rateLimit!.MaxRequestsPerSecond > 0);
    }
}
