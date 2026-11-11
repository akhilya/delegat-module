using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.Avito;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.YandexMarket;
using Delegat.Services.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class PluginCapabilitySmokeTests
{
    [Fact]
    public async Task AvitoPlugin_AdvertisesOnlyImplementedCatalogAndPriceOperations()
    {
        var plugin = new AvitoPlugin();

        Assert.Equal("avito", plugin.Key);
        Assert.True(plugin.IsSystem);
        Assert.Equal(PluginStability.Experimental, plugin.Stability);
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.GetCategories));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.GetCategoryFields));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ImportPrices));
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices));
        Assert.False(plugin.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct));
        Assert.Equal("price", Assert.Single(await plugin.GetPriceFieldsAsync(new ConnectionContext(), "goods")).Key);
        Assert.Equal(new[] { "clientId", "clientSecret" }, plugin.GetCredentialsSchema().Fields.Select(x => x.Key));
        Assert.True(plugin.GetCredentialsSchema().Fields[1].IsSecret);
    }

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

        var media = plugin.GetMediaCapabilities();
        Assert.True(media.CanImportVideos);
        Assert.True(media.CanExportVideos);
        Assert.True(media.UsesPermanentUrls);
        Assert.Equal(6, media.MaxVideos);

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

        var wildberriesMedia = Assert.IsAssignableFrom<IMarketplaceMediaCapabilities>(new WildberriesPlugin())
            .GetMediaCapabilities();
        Assert.True(wildberriesMedia.CanImportVideos);
        Assert.False(wildberriesMedia.CanExportVideos);
        Assert.Equal(1, wildberriesMedia.MaxVideos);
    }

    [Fact]
    public void AdapterWrapperDefaultsLegacyPluginsToNoVideoSupport()
    {
        var adapter = new PluginAdapterWrapper(new AvitoPlugin(), NullLoggerFactory.Instance);

        Assert.Equal(new MediaCapabilities(), adapter.GetMediaCapabilities());
    }

    [Fact]
    public void UnverifiedAdaptersNeverAdvertiseVideoExport()
    {
        IMarketplacePlugin[] plugins =
        [
            new OzonPlugin(), new AvitoPlugin(),
            new Delegat.Marketplace.OpenCart.OpenCartPlugin(),
            new Delegat.Marketplace.WooCommerce.WooCommercePlugin(),
            new Delegat.Marketplace.Medusa.MedusaPlugin()
        ];
        foreach (var plugin in plugins)
        {
            var adapter = new PluginAdapterWrapper(plugin, NullLoggerFactory.Instance);
            Assert.False(adapter.GetMediaCapabilities().CanExportVideos, plugin.Key);
        }
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
