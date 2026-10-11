using Delegat.Infrastructure.Data;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Fakes;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
            "background",
            CancellationToken.None);

        Assert.True(result.Pulled);
        Assert.False(result.Skipped);
        Assert.Equal(2, result.Upserted);
        Assert.Equal(2, adapter.GetOrdersCalls);
        Assert.Equal(2, await db.marketplaceOrders.CountAsync());
        Assert.True(adapter.GetOrdersQueries.TrueForAll(q =>
            q is { PageSize: 50, Status: MarketplaceOrderStatuses.AwaitingPack, From: null, To: null }));
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
    public async Task Cooldown_SkipsSecondPageLoadWithinFifteenSeconds()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("a-1")], hasMore: false),
            OrdersAdapterFake.Page([MakeOrder("a-2")], hasMore: false));

        var first = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(first.Pulled);
        Assert.Equal(1, adapter.GetOrdersCalls);

        time.UtcNow = time.UtcNow.Add(MarketplaceOrderSyncService.LiveCooldown - TimeSpan.FromSeconds(5));
        var second = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(second.Skipped);
        Assert.False(second.Pulled);
        Assert.Equal(1, adapter.GetOrdersCalls);

        time.UtcNow = time.UtcNow.AddSeconds(5);
        var third = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "hot", CancellationToken.None);
        Assert.True(third.Pulled);
        Assert.False(third.Skipped);
        Assert.Equal(2, adapter.GetOrdersCalls);
    }

    [Fact]
    public async Task HotPull_ReadsNewestPageOfRecentWindow()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("new-1")], hasMore: true, nextCursor: "2"),
            OrdersAdapterFake.Page([MakeOrder("older")], hasMore: false));

        var result = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);

        Assert.True(result.Pulled);
        Assert.Equal(1, adapter.GetOrdersCalls);
        Assert.Equal(["new-1"], await db.marketplaceOrders.Select(o => o.marketplaceOrderId).ToListAsync());
        var query = adapter.GetOrdersQueries.Single();
        Assert.Equal(time.UtcNow - MarketplaceOrderSyncService.HotWindow, query!.From);
        Assert.Equal(time.UtcNow.AddDays(1), query.To);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingPack, query.Status);
        var state = await db.marketplaceOrderSyncStates.SingleAsync();
        Assert.Equal(
            MarketplaceOrderSyncService.HotStateKey(MarketplaceOrderStatuses.AwaitingPack),
            state.status);
    }

    [Fact]
    public async Task Background_UsesFiveMinuteDeepCooldown()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("a-1")], hasMore: false),
            OrdersAdapterFake.Page([MakeOrder("a-2")], hasMore: false));

        var first = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "background", CancellationToken.None);
        Assert.True(first.Pulled);
        var callsAfterFirst = adapter.GetOrdersCalls;

        time.UtcNow = time.UtcNow.Add(MarketplaceOrderSyncService.DeepCooldown - TimeSpan.FromMinutes(1));
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
    public async Task PageHot_DoesNotResetDeepCooldown()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("hot-1")], hasMore: false),
            OrdersAdapterFake.Page([MakeOrder("deep-1")], hasMore: false));

        var page = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(page.Pulled);
        var callsAfterPage = adapter.GetOrdersCalls;

        time.UtcNow = time.UtcNow.AddMinutes(1);
        var background = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "background", CancellationToken.None);
        Assert.True(background.Pulled);
        Assert.False(background.Skipped);
        Assert.True(adapter.GetOrdersCalls > callsAfterPage);

        time.UtcNow = time.UtcNow.AddSeconds(1);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("hot-2")], hasMore: false));
        var again = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(again.Pulled);
        Assert.Equal(
            "hot-2",
            (await db.marketplaceOrders.SingleAsync(o => o.marketplaceOrderId == "hot-2")).marketplaceOrderId);
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
            s.connectionId == connection.Id
            && s.status == MarketplaceOrderSyncService.HotStateKey(MarketplaceOrderStatuses.AwaitingPack));
        Assert.Contains("ozon 500", state.lastError);
        Assert.NotNull(state.lastPulledAt);

        var callsAfterError = adapter.GetOrdersCalls;
        var third = await sync.SyncConnectionStatusesAsync(
            connection, [MarketplaceOrderStatuses.AwaitingPack], "page", CancellationToken.None);
        Assert.True(third.Skipped);
        Assert.False(third.Pulled);
        Assert.Equal(callsAfterError, adapter.GetOrdersCalls);
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

    [Fact]
    public async Task CancelledGetOrders_Throws_DoesNotRecordLastError()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, _, connection, _, _, _) = await CreateSutAsync(db);
        using var cts = new CancellationTokenSource();
        adapter.CancelOnGetOrders = cts;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sync.SyncConnectionStatusesAsync(
                connection,
                [MarketplaceOrderStatuses.AwaitingPack],
                "page",
                cts.Token));

        var state = await db.marketplaceOrderSyncStates.FirstOrDefaultAsync(s =>
            s.connectionId == connection.Id
            && s.status == MarketplaceOrderSyncService.HotStateKey(MarketplaceOrderStatuses.AwaitingPack));
        Assert.True(state == null || state.lastError == null);
    }

    [Fact]
    public async Task PullStatus_PageCap_StopsAtTwenty_SetsPageCapReason()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, _, connection, _, _, _) = await CreateSutAsync(db);
        var pages = Enumerable.Range(1, MarketplaceOrderSyncService.MaxPagesPerStatusPerRun + 1)
            .Select(i => OrdersAdapterFake.Page(
                [MakeOrder($"p-{i}")],
                hasMore: true,
                nextCursor: $"{i + 1}"))
            .ToArray();
        adapter.EnqueuePages(MarketplaceOrderStatuses.AwaitingPack, pages);

        var result = await sync.SyncConnectionStatusesAsync(
            connection,
            [MarketplaceOrderStatuses.AwaitingPack],
            "background",
            CancellationToken.None);

        Assert.Equal(MarketplaceOrderSyncService.MaxPagesPerStatusPerRun, adapter.GetOrdersCalls);
        Assert.Contains(MarketplaceOrderSyncService.PageCapReason, result.Reason);
        Assert.True(result.Pulled);
        Assert.Equal(MarketplaceOrderSyncService.MaxPagesPerStatusPerRun, result.Upserted);
    }

    [Fact]
    public async Task RefreshInFlight_SkipsIdsUpsertedInThisRun()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, _, connection, snapshots, _, _) = await CreateSutAsync(db);
        await snapshots.UpsertAsync(
            connection.storeId,
            connection.Id,
            MakeOrder("stored-2", status: MarketplaceOrderStatuses.AwaitingPack),
            CancellationToken.None);
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("listed-1")], hasMore: false));
        adapter.OrdersById["listed-1"] = MakeOrder("listed-1", status: MarketplaceOrderStatuses.Delivering);
        adapter.OrdersById["stored-2"] = MakeOrder("stored-2", status: MarketplaceOrderStatuses.Delivering);

        await sync.SyncConnectionStatusesAsync(
            connection,
            [MarketplaceOrderStatuses.AwaitingPack],
            "background",
            CancellationToken.None);

        Assert.Equal(1, adapter.GetOrderCalls);
        Assert.Equal(
            MarketplaceOrderStatuses.AwaitingPack,
            (await snapshots.FindAsync(connection.storeId, connection.Id, "listed-1", CancellationToken.None))!.status);
        Assert.Equal(
            MarketplaceOrderStatuses.Delivering,
            (await snapshots.FindAsync(connection.storeId, connection.Id, "stored-2", CancellationToken.None))!.status);
    }

    [Fact]
    public async Task RefreshInFlight_CapsIdLookupsPerRun()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, _, connection, _, _, _) = await CreateSutAsync(db);
        for (var i = 0; i < MarketplaceOrderSyncService.MaxInFlightIdRefreshesPerRun + 1; i++)
        {
            db.marketplaceOrders.Add(new MarketplaceOrder
            {
                storeId = connection.storeId,
                connectionId = connection.Id,
                marketplaceOrderId = $"cap-{i}",
                status = MarketplaceOrderStatuses.AwaitingPack,
                channel = "fbs",
                currency = "RUB",
                syncedAt = DateTime.UtcNow,
                payloadJson = "{}"
            });
        }

        await db.SaveChangesAsync();

        await sync.SyncConnectionStatusesAsync(
            connection,
            [MarketplaceOrderStatuses.AwaitingPack],
            "background",
            CancellationToken.None);

        Assert.Equal(MarketplaceOrderSyncService.MaxInFlightIdRefreshesPerRun, adapter.GetOrderCalls);
    }

    [Fact]
    public async Task RefreshInFlight_TakesUnlistedAfterExcludingThisRunUpserts()
    {
        await using var db = TestDbFactory.CreateDb();
        var (sync, adapter, time, connection, _, _, _) = await CreateSutAsync(db);
        var listed = Enumerable.Range(0, MarketplaceOrderSyncService.MaxInFlightIdRefreshesPerRun)
            .Select(i => MakeOrder($"listed-{i}"))
            .ToList();
        var listedSyncedAt = time.UtcNow.UtcDateTime;
        foreach (var order in listed)
        {
            db.marketplaceOrders.Add(new MarketplaceOrder
            {
                storeId = connection.storeId,
                connectionId = connection.Id,
                marketplaceOrderId = order.ExternalId,
                status = MarketplaceOrderStatuses.AwaitingPack,
                channel = "fbs",
                currency = "RUB",
                syncedAt = listedSyncedAt,
                payloadJson = "{}"
            });
        }

        db.marketplaceOrders.Add(new MarketplaceOrder
        {
            storeId = connection.storeId,
            connectionId = connection.Id,
            marketplaceOrderId = "unlisted-stale",
            status = MarketplaceOrderStatuses.AwaitingPack,
            channel = "fbs",
            currency = "RUB",
            syncedAt = listedSyncedAt.AddHours(-2),
            payloadJson = "{}"
        });
        await db.SaveChangesAsync();

        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page(listed, hasMore: false));
        adapter.OrdersById["unlisted-stale"] = MakeOrder("unlisted-stale", status: MarketplaceOrderStatuses.Delivering);

        await sync.SyncConnectionStatusesAsync(
            connection,
            [MarketplaceOrderStatuses.AwaitingPack],
            "background",
            CancellationToken.None);

        Assert.Equal(1, adapter.GetOrderCalls);
        Assert.Equal(["unlisted-stale"], adapter.GetOrderIds);
    }

    [Fact]
    public async Task Worker_PullsRecentWindowWhenDeepScanIsCoolingDown()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();

        var time = new FixedTimeProvider
        {
            UtcNow = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)
        };
        var adapter = new OrdersAdapterFake { Key = "ozon" };
        adapter.EnqueuePages(
            MarketplaceOrderStatuses.AwaitingPack,
            OrdersAdapterFake.Page([MakeOrder("from-deep")], hasMore: false),
            OrdersAdapterFake.Page([MakeOrder("from-hot")], hasMore: false));

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IMarketplaceAdapterRegistry>(new SingleAdapterRegistry(adapter));
        services.AddSingleton<ILogger<MarketplaceOrderSyncService>>(
            NullLogger<MarketplaceOrderSyncService>.Instance);
        services.AddScoped<MarketplaceOrderSnapshotService>();
        services.AddScoped<MarketplaceOrderSyncService>();
        using var provider = services.BuildServiceProvider();
        var worker = new OrderSyncBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrderSyncBackgroundService>.Instance);

        Assert.True(OrderSyncBackgroundService.Interval <= TimeSpan.FromSeconds(30));

        await worker.ProcessOnceAsync(CancellationToken.None);
        Assert.Equal(
            "from-deep",
            (await db.marketplaceOrders.SingleAsync()).marketplaceOrderId);
        var callsAfterDeep = adapter.GetOrdersCalls;
        Assert.True(callsAfterDeep > 1);

        time.UtcNow = time.UtcNow.Add(OrderSyncBackgroundService.Interval);
        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.True(adapter.GetOrdersCalls > callsAfterDeep);
        Assert.Equal(
            "from-hot",
            (await db.marketplaceOrders.SingleAsync(o => o.marketplaceOrderId == "from-hot")).marketplaceOrderId);
        var hotQuery = adapter.GetOrdersQueries.Last(q =>
            q?.Status == MarketplaceOrderStatuses.AwaitingPack && q.From != null);
        Assert.Equal(time.UtcNow - MarketplaceOrderSyncService.HotWindow, hotQuery!.From);
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
            time,
            NullLogger<MarketplaceOrderSyncService>.Instance);
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
