using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class MarketplaceOrderBucketsTests
{
    [Theory]
    [InlineData("assembly", new[] { "new", "unpaid", "confirmed", "awaiting_pack" })]
    [InlineData("Assembly", new[] { "new", "unpaid", "confirmed", "awaiting_pack" })]
    [InlineData("ship", new[] { "awaiting_deliver" })]
    [InlineData("delivery", new[] { "delivering" })]
    [InlineData("done", new[] { "delivered" })]
    public void StatusesForOverviewTab_MatchesDesign(string tab, string[] expected)
    {
        Assert.Equal(expected, MarketplaceOrderBuckets.StatusesForOverviewTab(tab));
    }

    [Fact]
    public void StatusesForOverviewTab_Unknown_IsEmpty()
    {
        Assert.Empty(MarketplaceOrderBuckets.StatusesForOverviewTab("closed"));
        Assert.Empty(MarketplaceOrderBuckets.StatusesForOverviewTab(""));
    }

    [Fact]
    public void ListStatusesForAdapter_HidesMissingMailboxes()
    {
        Assert.Contains(
            MarketplaceOrderStatuses.New,
            MarketplaceOrderBuckets.ListStatusesForAdapter("ozon"));
        Assert.Contains(
            MarketplaceOrderStatuses.Unpaid,
            MarketplaceOrderBuckets.ListStatusesForAdapter("ozon"));
        Assert.DoesNotContain(
            MarketplaceOrderStatuses.AwaitingDeliver,
            MarketplaceOrderBuckets.ListStatusesForAdapter("woocommerce"));
        Assert.Contains(
            MarketplaceOrderStatuses.New,
            MarketplaceOrderBuckets.ListStatusesForAdapter("yandex"));
        Assert.Contains(
            MarketplaceOrderStatuses.Cancelled,
            MarketplaceOrderBuckets.ListStatusesForAdapter("ozon"));
        Assert.DoesNotContain(
            MarketplaceOrderStatuses.New,
            MarketplaceOrderBuckets.ListStatusesForAdapter("wildberries"));
    }

    [Theory]
    [InlineData("delivered", true)]
    [InlineData("Delivered", true)]
    [InlineData("cancelled", true)]
    [InlineData("returned", true)]
    [InlineData("awaiting_pack", false)]
    public void IsTerminal(string status, bool expected)
        => Assert.Equal(expected, MarketplaceOrderBuckets.IsTerminal(status));
}
