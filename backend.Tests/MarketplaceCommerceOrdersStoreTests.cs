using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;
using Delegat.Tests.Fakes;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class MarketplaceCommerceOrdersStoreTests
{
    [Fact]
    public async Task ListStoredConnectionOrders_DoesNotCallAdapter()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();

        var time = TimeProvider.System;
        var snapshots = new MarketplaceOrderSnapshotService(db, time);
        await snapshots.UpsertAsync(store.Id, connection.Id, new ExternalOrder
        {
            ExternalId = "p-1",
            DisplayId = "1",
            Status = MarketplaceOrderStatuses.AwaitingPack,
            Channel = "fbs",
            Currency = "RUB",
            Total = 10
        }, CancellationToken.None);

        var adapter = new OrdersAdapterFake { Key = "ozon" };
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), snapshots);
        var (items, total, hasMore, _) = await commerce.ListStoredConnectionOrdersAsync(
            connection,
            new OrderQuery { Page = 1, PageSize = 50, Status = MarketplaceOrderStatuses.AwaitingPack },
            CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("p-1", items[0].ExternalId);
        Assert.Equal(1, total);
        Assert.False(hasMore);
        Assert.Equal(0, adapter.GetOrdersCalls);
    }

    [Fact]
    public async Task GetStoredOrder_PublicMiss_DoesNotCallAdapter()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();

        var adapter = new OrdersAdapterFake { Key = "ozon" };
        var snapshots = new MarketplaceOrderSnapshotService(db, TimeProvider.System);
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), snapshots);
        var order = await commerce.GetStoredOrderAsync(
            connection, adapter, "missing", liveFallback: false, CancellationToken.None);

        Assert.Null(order);
        Assert.Equal(0, adapter.GetOrderCalls);
    }

    [Fact]
    public async Task GetStoredOrder_CabinetMiss_CallsAdapterAndUpserts()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();

        var adapter = new OrdersAdapterFake { Key = "ozon" };
        adapter.OrdersById["live-1"] = new ExternalOrder
        {
            ExternalId = "live-1",
            Status = MarketplaceOrderStatuses.AwaitingPack,
            Channel = "fbs"
        };
        var snapshots = new MarketplaceOrderSnapshotService(db, TimeProvider.System);
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), snapshots);
        var order = await commerce.GetStoredOrderAsync(
            connection, adapter, "live-1", liveFallback: true, CancellationToken.None);

        Assert.Equal("live-1", order?.ExternalId);
        Assert.Equal(1, adapter.GetOrderCalls);
        Assert.NotNull(await snapshots.FindAsync(store.Id, connection.Id, "live-1", CancellationToken.None));
    }

    [Fact]
    public async Task CountStoredOverviewTabs_GroupsSnapshotsByTab()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();

        var snapshots = new MarketplaceOrderSnapshotService(db, TimeProvider.System);
        await snapshots.UpsertAsync(store.Id, connection.Id, new ExternalOrder
        {
            ExternalId = "a-1",
            Status = MarketplaceOrderStatuses.AwaitingPack,
            Channel = "fbs"
        }, CancellationToken.None);
        await snapshots.UpsertAsync(store.Id, connection.Id, new ExternalOrder
        {
            ExternalId = "s-1",
            Status = MarketplaceOrderStatuses.AwaitingDeliver,
            Channel = "fbs"
        }, CancellationToken.None);
        await snapshots.UpsertAsync(store.Id, connection.Id, new ExternalOrder
        {
            ExternalId = "d-1",
            Status = MarketplaceOrderStatuses.Delivered,
            Channel = "fbs"
        }, CancellationToken.None);

        var adapter = new OrdersAdapterFake { Key = "ozon" };
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), snapshots);
        var counts = await commerce.CountStoredOverviewTabsAsync(
            store.Id,
            new OrderQuery(),
            CancellationToken.None);

        Assert.Equal(1, counts["assembly"]);
        Assert.Equal(1, counts["ship"]);
        Assert.Equal(0, counts["delivery"]);
        Assert.Equal(1, counts["done"]);
    }
}
