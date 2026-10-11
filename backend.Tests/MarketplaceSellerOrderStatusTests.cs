using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class MarketplaceSellerOrderStatusTests
{
    [Fact]
    public void OzonPack_MovesAwaitingPackToReadyToShip()
    {
        var order = Order(MarketplaceOrderStatuses.AwaitingPack);
        MarketplaceSellerOrderStatus.Advance("ozon", MarketplaceOrderActions.Pack, order);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingDeliver, order.Status);
    }

    [Fact]
    public void OzonPack_KeepsReadBackThatAlreadyMoved()
    {
        var order = Order(MarketplaceOrderStatuses.AwaitingDeliver);
        MarketplaceSellerOrderStatus.Advance("ozon", MarketplaceOrderActions.Pack, order);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingDeliver, order.Status);
    }

    [Fact]
    public void WildberriesShip_MovesToDelivering()
    {
        var order = Order(MarketplaceOrderStatuses.AwaitingDeliver);
        MarketplaceSellerOrderStatus.Advance("wildberries", MarketplaceOrderActions.Ship, order);
        Assert.Equal(MarketplaceOrderStatuses.Delivering, order.Status);
    }

    [Fact]
    public void YandexPack_StaysInPacking()
    {
        var order = Order(MarketplaceOrderStatuses.Confirmed);
        MarketplaceSellerOrderStatus.Advance("yandex", MarketplaceOrderActions.Pack, order);
        Assert.Equal(MarketplaceOrderStatuses.Confirmed, order.Status);
    }

    [Fact]
    public void WooPack_DoesNotHideTheOrderFromItsMailboxes()
    {
        var order = Order(MarketplaceOrderStatuses.AwaitingPack);
        MarketplaceSellerOrderStatus.Advance("woocommerce", MarketplaceOrderActions.Pack, order);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingPack, order.Status);
    }

    [Fact]
    public void StaleList_CannotUndoARecentSellerStatus_UnlessCancelled()
    {
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(MarketplaceSellerOrderStatus.ShouldKeepStoredStatus(
            MarketplaceOrderStatuses.AwaitingDeliver,
            now,
            MarketplaceOrderStatuses.AwaitingPack,
            now.AddSeconds(30)));
        Assert.False(MarketplaceSellerOrderStatus.ShouldKeepStoredStatus(
            MarketplaceOrderStatuses.AwaitingDeliver,
            now,
            MarketplaceOrderStatuses.Cancelled,
            now.AddSeconds(30)));
    }

    [Fact]
    public void Cancel_AlwaysClosesTheOrder()
    {
        var order = Order(MarketplaceOrderStatuses.Delivering);
        MarketplaceSellerOrderStatus.Advance("ozon", MarketplaceOrderActions.Cancel, order);
        Assert.Equal(MarketplaceOrderStatuses.Cancelled, order.Status);
    }

    private static ExternalOrder Order(string status) => new()
    {
        ExternalId = "1",
        Status = status
    };
}
