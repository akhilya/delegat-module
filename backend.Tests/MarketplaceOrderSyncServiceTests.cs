using Delegat.Infrastructure.Data;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Delegat.Tests.Fakes;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class MarketplaceOrderSyncServiceTests
{
    [Fact]
    public async Task PullStatus_PaginatesInFlightUntilHasMoreFalse()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, _, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("a-1")], hasMore: true, nextCursor: "2"),
            OrdersAdapterFake.Page([MakeOrder("a-2")], hasMore: false));

        var result = await sync.SyncConnectionStatusesAsync(
            connection,
            [MarketplaceOrderStatuses.AwaitingPack],
            "page",
            CancellationToken.None);

        Assert.True(result.Pulled);
        Assert.False(result.Skipped);
        Assert.Equal(2, result.Upserted);
        Assert.Equal(2, adapter.GetOrdersCalls);
        Assert.Equal(2, await db.marketplaceOrders.CountAsync());
        Assert.True(adapter.GetOrdersQueries.TrueForAll(q => q is { PageSize: 50, Status: MarketplaceOrderStatuses.AwaitingPack }));
        var ids = await db.marketplaceOrders.Select(o => o.marketplaceOrderId).OrderBy(id => id).ToListAsync();
        Assert.Equal(["a-1", "a-2"], ids);
    }

    [Fact]
    public async Task PullStatus_Terminal_OnlyFirstPage()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, _, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.Delivered,
            OrdersAdapterFake.Page(
                [MakeOrder("d-1", status: MarketplaceOrderStatuses.Delivered)],
                hasMore: true,
                nextCursor: "2"),
            OrdersAdapterFake.Page(
                [MakeOrder("d-2", status: MarketplaceOrderStatuses.Delivered)],
                hasMore: false));

        var result = await sync.SyncConnectionStatusesAsync(
            connection,
            [MarketplaceOrderStatuses.Delivered],
            "page",
            CancellationToken.None);

        Assert.True(result.Pulled);
        Assert.Equal(1, adapter.GetOrdersCalls);
        Assert.Equal(1, result.Upserted);
        Assert.Equal("d-1", (await db.marketplaceOrders.SingleAsync()).marketplaceOrderId);
        Assert.False(await db.marketplaceOrders.AnyAsync(o => o.marketplaceOrderId == "d-2"));
    }

    [Fact]
    public async Task Cooldown_SkipsSecondPageLoadWithinFiveMinutes()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("a-1")], hasMore: false));

        var first = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(first.Pulled);
        Assert.Equal(1, adapter.GetOrdersCalls);

        time.UtcNow = time.UtcNow.AddMinutes(4);
        var second = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(second.Skipped);
        Assert.False(second.Pulled);
        Assert.Equal(1, adapter.GetOrdersCalls);

        time.UtcNow = time.UtcNow.AddMinutes(1);
        var third = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(third.Pulled);
        Assert.False(third.Skipped);
        Assert.Equal(2, adapter.GetOrdersCalls);
    }

    [Fact]
    public async Task Background_UsesFifteenMinuteCooldown()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("a-1")], hasMore: false));

        var first = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "background", CancellationToken.None);
        Assert.True(first.Pulled);
        var callsAfterFirst = adapter.GetOrdersCalls;

        time.UtcNow = time.UtcNow.AddMinutes(14);
        var second = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "background", CancellationToken.None);
        Assert.True(second.Skipped);
        Assert.False(second.Pulled);
        Assert.Equal(callsAfterFirst, adapter.GetOrdersCalls);

        time.UtcNow = time.UtcNow.AddMinutes(1);
        var third = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "background", CancellationToken.None);
        Assert.True(third.Pulled);
        Assert.True(adapter.GetOrdersCalls > callsAfterFirst);
    }

    [Fact]
    public async Task PageLoad_UpdatesTimestampSoBackgroundSkips()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("a-1")], hasMore: false));

        var page = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(page.Pulled);
        var callsAfterPage = adapter.GetOrdersCalls;

        time.UtcNow = time.UtcNow.AddMinutes(1);
        var background = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "background", CancellationToken.None);
        Assert.Equal(callsAfterPage, adapter.GetOrdersCalls);
        Assert.False(background.Pulled);
        Assert.True(background.Skipped);
    }

    [Fact]
    public async Task PullError_LeavesExistingSnapshot_RecordsLastError()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("keep-1")], hasMore: false));

        var first = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(first.Pulled);
        Assert.Equal(1, await db.marketplaceOrders.CountAsync());

        adapter.GetOrdersException = new InvalidOperationException("ozon 500");
        time.UtcNow = time.UtcNow.AddMinutes(5);
        var second = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);

        Assert.False(second.Pulled);
        Assert.False(second.Skipped);
        Assert.NotNull(second.Reason);
        Assert.Equal(1, await db.marketplaceOrders.CountAsync());
        Assert.Equal("keep-1", (await db.marketplaceOrders.SingleAsync()).marketplaceOrderId);
        var state = await db.marketplaceOrderSyncStates.SingleAsync(s =>
            s.connectionId == connection.Id && s.status == MarketplaceOrderStatuses.AwaitingPack);
        Assert.Contains("ozon 500", state.lastError);
    }

    [Fact]
    public async Task RefreshInFlightById_AfterLists()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, _, connection, snapshots, _, _) = await CreateSutAsync(db);
        var stored = MakeOrder("x-1", status: MarketplaceOrderStatuses.AwaitingPack);
        await snapshots.UpsertAsync(connection.storeId, connection.Id, stored, CancellationToken.None);
        adapter.OrdersById["x-1"] = MakeOrder("x-1", status: MarketplaceOrderStatuses.Delivering);

        await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.Equal(0, adapter.GetOrderCalls);
        Assert.Equal(
            MarketplaceOrderStatuses.AwaitingPack,
            (await snapshots.FindAsync(connection.storeId, connection.Id, "x-1", CancellationToken.None))!.status);

        await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "background", CancellationToken.None);
        Assert.True(adapter.GetOrderCalls >= 1);
        var updated = await snapshots.FindAsync(connection.storeId, connection.Id, "x-1", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(MarketplaceOrderStatuses.Delivering, updated.status);
    }

    [Fact]
    public async Task SyncTab_AllActiveGetOrdersConnections()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, _, first, _, data, store) = await CreateSutAsync(db);
        var second = data.AddConnection(store.Id, "ozon", isActive: true);
        var inactive = data.AddConnection(store.Id, "ozon", isActive: false);
        await data.SaveAsync();

        var result = await sync.SyncStoreTabAsync(store.Id, "assembly", "page", CancellationToken.None);

        Assert.True(result.Pulled);
        Assert.Contains(first.Id, adapter.GetOrdersConnectionIds);
        Assert.Contains(second.Id, adapter.GetOrdersConnectionIds);
        Assert.DoesNotContain(inactive.Id, adapter.GetOrdersConnectionIds);
    }

    private static async Task<(
        MarketplaceOrderSyncService Sync,
        OrdersAdapterFake Adapter,
        FixedTimeProvider Time,
        MarketplaceConnection Connection,
        MarketplaceOrderSnapshotService Snapshots,
        TestDataBuilder Data,
        Store Store)> CreateSutAsync(DataContext db)
    {
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();
        var time = new FixedTimeProvider
        {
            UtcNow = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)
        };
        var adapter = new OrdersAdapterFake { Key = "ozon" };
        var snapshots = new MarketplaceOrderSnapshotService(db, time);
        var sync = new MarketplaceOrderSyncService(
            db,
            new SingleAdapterRegistry(adapter),
            snapshots,
            time);
        return (sync, adapter, time, connection, snapshots, data, store);
    }

    private static ExternalOrder MakeOrder(
        string externalId,
        string? displayId = null,
        string status = MarketplaceOrderStatuses.AwaitingPack)
    {
        return new ExternalOrder
        {
            ExternalId = externalId,
            DisplayId = displayId ?? externalId,
            Status = status,
            Channel = "fbs",
            Currency = "RUB",
            Total = 100m,
            CreatedAt = new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero)
        };
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
