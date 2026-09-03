using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;
using HostCaps = Delegat.Services.Marketplace.MarketplaceCapabilities;
using SdkCaps = Delegat.Marketplace.SDK.MarketplaceCapabilities;

namespace Delegat.Tests;

public class MarketplaceCommerceServiceTests
{
    [Fact]
    public void BuildOrderQuery_CopiesFilters()
    {
        var from = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-01-31T00:00:00Z");
        var query = MarketplaceCommerceService.BuildOrderQuery(
            2, 25, "cursor-1", MarketplaceOrderStatuses.New, from, to, "fbs", "123");

        Assert.Equal(2, query.Page);
        Assert.Equal(25, query.PageSize);
        Assert.Equal("cursor-1", query.Cursor);
        Assert.Equal(MarketplaceOrderStatuses.New, query.Status);
        Assert.Equal(from, query.From);
        Assert.Equal(to, query.To);
        Assert.Equal("fbs", query.Channel);
        Assert.Equal("123", query.Search);
    }

    [Fact]
    public void HostAndSdkPromotionFlags_ShareTheSameBits()
    {
        Assert.Equal((int)SdkCaps.GetOrders, (int)HostCaps.GetOrders);
        Assert.Equal((int)SdkCaps.UpdateOrderStatus, (int)HostCaps.UpdateOrderStatus);
        Assert.Equal((int)SdkCaps.GetPromotions, (int)HostCaps.GetPromotions);
        Assert.Equal((int)SdkCaps.UpdatePromotions, (int)HostCaps.UpdatePromotions);
        Assert.Equal((int)SdkCaps.GetOrderLabels, (int)HostCaps.GetOrderLabels);
        Assert.Equal(1 << 22, (int)SdkCaps.GetPromotions);
        Assert.Equal(1 << 23, (int)SdkCaps.UpdatePromotions);
        Assert.Equal(1 << 24, (int)SdkCaps.GetOrderLabels);
    }

    [Fact]
    public void ApplyLocalOrderItemImages_FillsMissingFromLinkKeys()
    {
        var order = new ExternalOrder
        {
            Items =
            [
                new ExternalOrderItem { ExternalProductId = "nm-1", Sku = "ART" },
                new ExternalOrderItem
                {
                    ExternalProductId = "keep",
                    ImageUrl = "https://cdn.example/already.webp"
                }
            ]
        };

        MarketplaceCommerceService.ApplyLocalOrderItemImages(
            [order],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ART"] = "/api/files/9/thumbnail?w=256"
            });

        Assert.Equal("/api/files/9/thumbnail?w=256", order.Items[0].ImageUrl);
        Assert.Equal("https://cdn.example/already.webp", order.Items[1].ImageUrl);
    }
}
