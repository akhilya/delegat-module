using Delegat.Infrastructure.Data;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class MarketplaceOrderSnapshotServiceTests
{
    [Fact]
    public async Task Upsert_InsertsThenUpdatesStatusAndPayload()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, time, store, connection) = await CreateSutAsync(db);
        var order = MakeOrder("123-1", displayId: "123", status: MarketplaceOrderStatuses.AwaitingPack);

        await svc.UpsertAsync(store.Id, connection.Id, order, CancellationToken.None);

        Assert.Equal(1, await db.marketplaceOrders.CountAsync());
        var inserted = await db.marketplaceOrders.SingleAsync();
        Assert.Equal("123-1", inserted.marketplaceOrderId);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingPack, inserted.status);
        Assert.Equal(time.UtcNow.UtcDateTime, inserted.syncedAt);
        var firstPayload = inserted.payloadJson;
        var firstSyncedAt = inserted.syncedAt;

        time.UtcNow = time.UtcNow.AddMinutes(5);
        order.Status = MarketplaceOrderStatuses.AwaitingDeliver;
        order.Comment = "packed";

        await svc.UpsertAsync(store.Id, connection.Id, order, CancellationToken.None);

        Assert.Equal(1, await db.marketplaceOrders.CountAsync());
        var updated = await db.marketplaceOrders.SingleAsync();
        Assert.Equal(MarketplaceOrderStatuses.AwaitingDeliver, updated.status);
        Assert.NotEqual(firstPayload, updated.payloadJson);
        Assert.Contains("packed", updated.payloadJson);
        Assert.Equal(time.UtcNow.UtcDateTime, updated.syncedAt);
        Assert.NotEqual(firstSyncedAt, updated.syncedAt);
    }

    [Fact]
    public async Task UpsertMany_InsertsAndUpdatesInOneBatch()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, time, store, connection) = await CreateSutAsync(db);

        await svc.UpsertManyAsync(
            store.Id,
            connection.Id,
            [
                MakeOrder("a-1"),
                MakeOrder("b-1"),
                new ExternalOrder { ExternalId = "  ", Status = MarketplaceOrderStatuses.New }
            ],
            CancellationToken.None);

        Assert.Equal(2, await db.marketplaceOrders.CountAsync());

        time.UtcNow = time.UtcNow.AddMinutes(1);
        await svc.UpsertManyAsync(
            store.Id,
            connection.Id,
            [
                MakeOrder("a-1", status: MarketplaceOrderStatuses.Delivered),
                MakeOrder("c-1")
            ],
            CancellationToken.None);

        Assert.Equal(3, await db.marketplaceOrders.CountAsync());
        var updated = await db.marketplaceOrders.SingleAsync(o => o.marketplaceOrderId == "a-1");
        Assert.Equal(MarketplaceOrderStatuses.Delivered, updated.status);
        Assert.Equal(time.UtcNow.UtcDateTime, updated.syncedAt);
        Assert.True(await db.marketplaceOrders.AnyAsync(o => o.marketplaceOrderId == "c-1"));
    }

    [Fact]
    public async Task Upsert_BlankExternalId_IsNoOp()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, _, store, connection) = await CreateSutAsync(db);

        await svc.UpsertAsync(
            store.Id,
            connection.Id,
            new ExternalOrder { ExternalId = "  ", Status = MarketplaceOrderStatuses.New },
            CancellationToken.None);

        Assert.Equal(0, await db.marketplaceOrders.CountAsync());
    }

    [Fact]
    public async Task List_FiltersByStatusesChannelSearchAndPage()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, _, store, connection) = await CreateSutAsync(db);
        var t1 = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("111-1", displayId: "find-me-1", status: MarketplaceOrderStatuses.AwaitingPack, created: t1),
            CancellationToken.None);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("222-1", displayId: "find-me-2", status: MarketplaceOrderStatuses.AwaitingPack, created: t2),
            CancellationToken.None);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("333-1", displayId: "find-me-3", status: MarketplaceOrderStatuses.Delivered, created: t2),
            CancellationToken.None);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("444-1", displayId: "find-me-4", status: MarketplaceOrderStatuses.AwaitingPack, channel: "FBO", created: t2),
            CancellationToken.None);

        var statuses = new[] { MarketplaceOrderStatuses.AwaitingPack };
        var listed = await svc.ListAsync(
            store.Id, connection.Id, statuses, "fbs", "find-me", from: null, to: null, page: 1, pageSize: 1, CancellationToken.None);
        var count = await svc.CountAsync(
            store.Id, connection.Id, statuses, "fbs", "find-me", from: null, to: null, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.True(count > 1 * 1);
        Assert.Single(listed);
        Assert.Equal("222-1", listed[0].marketplaceOrderId);
    }

    [Fact]
    public async Task Get_MatchesMarketplaceIdOrDisplayId()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, _, store, connection) = await CreateSutAsync(db);
        await svc.UpsertAsync(
            store.Id,
            connection.Id,
            MakeOrder("Posting-1", displayId: "Cabinet-9"),
            CancellationToken.None);

        var byPosting = await svc.FindAsync(store.Id, connection.Id, "Posting-1", CancellationToken.None);
        var byDisplay = await svc.FindAsync(store.Id, connection.Id, "Cabinet-9", CancellationToken.None);
        var missing = await svc.FindAsync(store.Id, connection.Id, "no-such", CancellationToken.None);
        var wrongCase = await svc.FindAsync(store.Id, connection.Id, "posting-1", CancellationToken.None);

        Assert.NotNull(byPosting);
        Assert.Equal("Posting-1", byPosting.marketplaceOrderId);
        Assert.NotNull(byDisplay);
        Assert.Equal("Posting-1", byDisplay.marketplaceOrderId);
        Assert.Null(missing);
        Assert.Null(wrongCase);
    }

    [Fact]
    public async Task List_ChannelFilter_IgnoresCase()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, _, store, connection) = await CreateSutAsync(db);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("111-1", displayId: "111", channel: "fbs"),
            CancellationToken.None);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("222-1", displayId: "222", channel: "FBO"),
            CancellationToken.None);

        var listed = await svc.ListAsync(
            store.Id, connection.Id, [], "FBS", null, from: null, to: null, page: 1, pageSize: 10, CancellationToken.None);

        Assert.Single(listed);
        Assert.Equal("111-1", listed[0].marketplaceOrderId);
    }

    [Fact]
    public void ChannelFilter_UsesToLower_NotStringComparison()
    {
        var src = System.IO.File.ReadAllText(FindRepoFile("backend", "Services", "Marketplace", "MarketplaceOrderSnapshotService.cs"));
        Assert.DoesNotContain("StringComparison.OrdinalIgnoreCase", src);
        Assert.Contains("ToLower()", src);
    }

    [Fact]
    public async Task Find_PrefersMarketplaceOrderId_WhenDisplayIdsMatch()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, _, store, connection) = await CreateSutAsync(db);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("76728543-0063-2", displayId: "76728543-0063"),
            CancellationToken.None);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("76728543-0063-1", displayId: "76728543-0063"),
            CancellationToken.None);

        var byPosting = await svc.FindAsync(store.Id, connection.Id, "76728543-0063-1", CancellationToken.None);
        Assert.NotNull(byPosting);
        Assert.Equal("76728543-0063-1", byPosting.marketplaceOrderId);
    }

    [Fact]
    public async Task Find_MarketplaceOrderId_WinsOverAnotherRowsDisplayId()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, _, store, connection) = await CreateSutAsync(db);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("76728543-0063-2", displayId: "76728543-0063-1"),
            CancellationToken.None);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("76728543-0063-1", displayId: "76728543-0063"),
            CancellationToken.None);

        var found = await svc.FindAsync(store.Id, connection.Id, "76728543-0063-1", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal("76728543-0063-1", found.marketplaceOrderId);
    }

    [Fact]
    public async Task List_PageSizeZero_DoesNotThrow()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, _, store, connection) = await CreateSutAsync(db);
        await svc.UpsertAsync(
            store.Id, connection.Id,
            MakeOrder("111-1", displayId: "111"),
            CancellationToken.None);

        var listed = await svc.ListAsync(
            store.Id, connection.Id, [], null, null, from: null, to: null, page: 1, pageSize: 0, CancellationToken.None);

        Assert.Single(listed);
        Assert.Equal("111-1", listed[0].marketplaceOrderId);
    }

    [Fact]
    public async Task ToExternalOrder_RestoresExternalIdStatusAndItems()
    {
        await using var db = TestDbFactory.CreateDb();
        var (svc, _, store, connection) = await CreateSutAsync(db);
        var order = MakeOrder("123-1", displayId: "123", status: MarketplaceOrderStatuses.AwaitingPack);
        order.Items =
        [
            new ExternalOrderItem
            {
                ExternalProductId = "p-1",
                Sku = "SKU-1",
                Name = "Widget",
                Quantity = 2,
                Price = 10.5m
            }
        ];

        await svc.UpsertAsync(store.Id, connection.Id, order, CancellationToken.None);
        var row = await db.marketplaceOrders.SingleAsync();
        var restored = svc.ToExternalOrder(row);

        Assert.Equal("123-1", restored.ExternalId);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingPack, restored.Status);
        Assert.Single(restored.Items);
        Assert.Equal("p-1", restored.Items[0].ExternalProductId);
        Assert.Equal("SKU-1", restored.Items[0].Sku);
        Assert.Equal("Widget", restored.Items[0].Name);
        Assert.Equal(2, restored.Items[0].Quantity);
        Assert.Equal(10.5m, restored.Items[0].Price);
    }

    [Fact]
    public void ToExternalOrder_BrokenJson_BuildsFromColumns()
    {
        using var db = TestDbFactory.CreateDb();
        var svc = new MarketplaceOrderSnapshotService(db, new FixedTimeProvider());
        var row = new MarketplaceOrder
        {
            marketplaceOrderId = "999-1",
            displayId = "999",
            status = MarketplaceOrderStatuses.Delivering,
            channel = "fbs",
            currency = "RUB",
            total = 42m,
            payloadJson = "{not-json"
        };

        var restored = svc.ToExternalOrder(row);

        Assert.Equal("999-1", restored.ExternalId);
        Assert.Equal("999", restored.DisplayId);
        Assert.Equal(MarketplaceOrderStatuses.Delivering, restored.Status);
        Assert.Equal("fbs", restored.Channel);
        Assert.Equal(42m, restored.Total);
        Assert.Empty(restored.Items);
    }

    private static async Task<(
        MarketplaceOrderSnapshotService Svc,
        FixedTimeProvider Time,
        Store Store,
        MarketplaceConnection Connection)> CreateSutAsync(DataContext db)
    {
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        await data.SaveAsync();
        var time = new FixedTimeProvider
        {
            UtcNow = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)
        };
        return (new MarketplaceOrderSnapshotService(db, time), time, store, connection);
    }

    private static ExternalOrder MakeOrder(
        string externalId,
        string? displayId = null,
        string status = MarketplaceOrderStatuses.AwaitingPack,
        string channel = "fbs",
        DateTimeOffset? created = null)
    {
        return new ExternalOrder
        {
            ExternalId = externalId,
            DisplayId = displayId,
            Status = status,
            Channel = channel,
            Currency = "RUB",
            Total = 100m,
            CreatedAt = created ?? new DateTimeOffset(2026, 1, 15, 8, 0, 0, TimeSpan.Zero)
        };
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (System.IO.File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
