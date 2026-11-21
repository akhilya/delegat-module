using System.Reflection;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.Medusa;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.WooCommerce;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Integration;
using Delegat.Services.Marketplace;
using Delegat.Services.Tools;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HostPromoPage = Delegat.Models.Marketplace.DTOs.PagedResult<Delegat.Marketplace.SDK.DTOs.ExternalPromotion>;
using HostGroupPage = Delegat.Models.Marketplace.DTOs.PagedResult<Delegat.Models.Marketplace.DTOs.ExternalGroup>;

namespace Delegat.Tests;

public sealed class IntegrationExecutionP6Tests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoopOpLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null)
            => Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }

    private sealed class SingleAdapterRegistry(IMarketplaceAdapter? adapter) : IMarketplaceAdapterRegistry
    {
        public IMarketplaceAdapter? GetByKey(string key) => adapter;
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => adapter is null ? [] : [adapter];
        public bool Exists(string key) => adapter is not null;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private class GroupImportAdapterProxy : DispatchProxy
    {
        public Func<string?, Task<HostGroupPage>>? OnImportGroups { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IMarketplaceAdapter.Capabilities)}")
                return Delegat.Services.Marketplace.MarketplaceCapabilities.ImportGroups;
            if (targetMethod?.Name == nameof(IMarketplaceAdapter.ImportGroupsAsync))
                return OnImportGroups!(args?[2] as string);
            throw new NotImplementedException(targetMethod?.Name);
        }

        public static IMarketplaceAdapter Create(Func<string?, Task<HostGroupPage>> onImport)
        {
            var proxy = Create<IMarketplaceAdapter, GroupImportAdapterProxy>();
            ((GroupImportAdapterProxy)(object)proxy).OnImportGroups = onImport;
            return proxy;
        }
    }

    private static MarketplaceConnection CreateConnection(string adapterKey, string credentialsJson) => new()
    {
        Id = 10,
        storeId = 1,
        name = $"{adapterKey}-shop",
        adapterKey = adapterKey,
        credentialsJson = credentialsJson,
        isActive = true
    };

    [Fact]
    public async Task PromotionAction_V1Admit_WooCommerceCouponDeltaAndEmptyRejectedBeforeAdmission()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(null), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new WooCommercePlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);

        var conn = CreateConnection("woocommerce", """{"siteUrl":"https://woo.example","apiKey":"woo-k"}""");

        // Empty promotion update is blocked before admission.
        var emptyEx = await Assert.ThrowsAsync<ApiException>(() => exec.AdmitPromotionActionAsync(
            conn, "coupon-10", new PromotionProductsUpdate(), 7, null, "req-promo-empty", CancellationToken.None));
        Assert.Equal(400, emptyEx.StatusCode);
        Assert.Equal("promotion_update_empty", emptyEx.Code);
        Assert.Empty(await db.integrationCommands.AsNoTracking().ToListAsync());

        // Valid coupon add/remove delta admits and deduplicates on Idempotency-Key.
        var body = new PromotionProductsUpdate
        {
            Add = [new PromotionProductRef { ExternalProductId = "101" }],
            RemoveExternalProductIds = ["202"]
        };
        var first = await exec.AdmitPromotionActionAsync(conn, "coupon-10", body, 7, null, "req-promo-1", CancellationToken.None);
        var second = await exec.AdmitPromotionActionAsync(conn, "coupon-10", body, 7, null, "req-promo-1", CancellationToken.None);

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.CommandId, second.CommandId);

        var stages = await db.integrationStages.AsNoTracking().ToListAsync();
        var stage = Assert.Single(stages);
        Assert.Contains("101", stage.payloadJson);
        Assert.Contains("202", stage.payloadJson);
    }

    [Fact]
    public async Task PromotionAction_V1Admit_OzonMultiItemAddAndRemoveCreatesIndependentStages()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(null), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new OzonPlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);

        var conn = CreateConnection("ozon", """{"clientId":"123","apiKey":"oz-k"}""");
        var body = new PromotionProductsUpdate
        {
            Add = [new PromotionProductRef { ExternalProductId = "5001", ActionPrice = 799m }],
            RemoveExternalProductIds = ["5002"]
        };

        var admitted = await exec.AdmitPromotionActionAsync(conn, "901", body, 7, null, "req-oz-promo", CancellationToken.None);
        Assert.False(admitted.IsDuplicate);

        var stages = await db.integrationStages.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, stages.Count);
        Assert.Contains(stages, s => s.stageKey.Contains("activate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(stages, s => s.stageKey.Contains("deactivate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GroupUpdate_V1Admit_MedusaGroupUpsertValidatesAndAdmits()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(null), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new MedusaPlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);

        var conn = CreateConnection("medusa", """{"apiBaseUrl":"https://medusa.example/admin/delegat","apiKey":"mk-1"}""");

        var admitted = await exec.AdmitGroupUpdateAsync(
            conn, "grp-summer",
            new Dictionary<string, object?> { ["group_id"] = "grp-summer", ["name"] = "Summer Collection" },
            "Upsert Medusa group", 7, null, "req-grp-1", CancellationToken.None);

        Assert.False(admitted.IsDuplicate);
        var stage = Assert.Single(await db.integrationStages.AsNoTracking().ToListAsync());
        Assert.Equal("Grouping", stage.kind);
        Assert.Contains("Summer Collection", stage.payloadJson);
    }

    [Fact]
    public async Task GroupImport_StalledOrMissingNextCursor_FailsInsteadOfLoopingOrReportingComplete()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;

        db.stores.Add(new Models.Store { Id = 1, name = "Store 1" });
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = 10,
            storeId = 1,
            name = "Ozon",
            adapterKey = "ozon",
            credentialsJson = "{}",
            isActive = true
        });
        await db.SaveChangesAsync();

        // Case 1: HasMore=true with repeated cursor -> fails immediately on stall without saving partial ok.
        var stalledCalls = 0;
        var stalledAdapter = GroupImportAdapterProxy.Create(_ =>
        {
            stalledCalls++;
            return Task.FromResult(new HostGroupPage([], true, "same-cursor"));
        });

        var svcStalled = new GroupSyncService(db, new SingleAdapterRegistry(stalledAdapter), NullLogger<GroupSyncService>.Instance);
        var stalledResult = await svcStalled.ImportGroupsAsync(10, 1, CancellationToken.None);

        Assert.False(stalledResult.IsSuccess);
        Assert.Contains("stalled", stalledResult.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, stalledCalls);

        // Case 2: HasMore=true with missing NextCursor -> fails instead of looping.
        var missingCursorAdapter = GroupImportAdapterProxy.Create(_ =>
            Task.FromResult(new HostGroupPage([], true, null)));
        var svcMissing = new GroupSyncService(db, new SingleAdapterRegistry(missingCursorAdapter), NullLogger<GroupSyncService>.Instance);
        var missingResult = await svcMissing.ImportGroupsAsync(10, 1, CancellationToken.None);

        Assert.False(missingResult.IsSuccess);
        Assert.Contains("nextCursor", missingResult.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromotionMembershipClearer_StalledCursorAndEmptyHasMore_ReportIncompleteNeverSilentComplete()
    {
        // Stalled cursor on page 2 must report Incomplete=true with reason cursor_stalled.
        var calls = 0;
        var summaryStalled = await PromotionMembershipClearer.ClearAsync(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(new HostPromoPage(
                    [new ExternalPromotion { ExternalId = $"p-{calls}", Name = "Promo" }],
                    true,
                    "stuck-token"));
            },
            (_, _, _) => Task.FromResult(new PromotionProductsResult { Success = true, Removed = 1 }));

        Assert.True(summaryStalled.Incomplete);
        Assert.Equal("cursor_stalled", summaryStalled.IncompleteReason);
        Assert.Equal(2, calls);

        // Empty page with HasMore=true and no cursor must report Incomplete=true.
        var summaryEmpty = await PromotionMembershipClearer.ClearAsync(
            (_, _) => Task.FromResult(new HostPromoPage([], true, null)),
            (_, _, _) => Task.FromResult(new PromotionProductsResult { Success = true, Removed = 0 }));

        Assert.True(summaryEmpty.Incomplete);
        Assert.Equal("empty_page_with_has_more", summaryEmpty.IncompleteReason);

        // Clean finite list reports Incomplete=false.
        var summaryClean = await PromotionMembershipClearer.ClearAsync(
            (_, _) => Task.FromResult(new HostPromoPage(
                [new ExternalPromotion { ExternalId = "p-1", Name = "Promo 1" }],
                false,
                null)),
            (_, _, _) => Task.FromResult(new PromotionProductsResult { Success = true, Removed = 3 }));

        Assert.False(summaryClean.Incomplete);
        Assert.Null(summaryClean.IncompleteReason);
        Assert.Equal(3, summaryClean.Removed);
    }

    [Fact]
    public async Task ReviewRegressions_WooRoutePolicy_StablePromoItemKey_AndZeroOrphanGroupsOnBadCursor()
    {
        // 1. WooCommerce /wp-json/delegat/v1/ coupon route is permitted by host route policy catalog.
        var wooBinding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.WooCommerce, IntegrationProviderEnvironment.Production,
            new Uri("https://shop.example.com/wp-json/delegat/v1"), VerifiedSellerId: "conn:10");
        var policy = IntegrationRoutePolicyCatalog.Resolve(
            wooBinding,
            new IntegrationTransportRequest(
                "POST", new Uri("https://shop.example.com/wp-json/delegat/v1/promotions/coupon-10/products"),
                new Dictionary<string, string>(), "{}",
                new IntegrationRequestPolicy("woo:bridge", "op-1", IsEffectful: true)));
        Assert.NotNull(policy);
        Assert.True(policy!.IsEffectful);

        // 2. Ozon activation and later removal of the same product share identical itemKey ("901:5001"),
        //    and duplicate add entries deduplicate instead of failing with duplicate_item_key.
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(null), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new OzonPlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);
        var conn = CreateConnection("ozon", """{"clientId":"123","apiKey":"oz-k"}""");

        await exec.AdmitPromotionActionAsync(conn, "901", new PromotionProductsUpdate
        {
            Add =
            [
                new PromotionProductRef { ExternalProductId = "5001", ActionPrice = 899m },
                new PromotionProductRef { ExternalProductId = " 5001 ", ActionPrice = 799m }
            ]
        }, 7, null, "req-add-5001", CancellationToken.None);

        await exec.AdmitPromotionActionAsync(conn, "901", new PromotionProductsUpdate
        {
            RemoveExternalProductIds = ["5001"]
        }, 7, null, "req-rem-5001", CancellationToken.None);

        var stages = await db.integrationStages.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, stages.Count);
        Assert.Equal("901:5001", stages[0].itemKey);
        Assert.Equal("901:5001", stages[1].itemKey);

        // 3. Populated group page with missing NextCursor persists ZERO groups (no orphan on retry).
        db.stores.Add(new Models.Store { Id = 1, name = "Store 1" });
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = 10, storeId = 1, name = "Ozon", adapterKey = "ozon", credentialsJson = "{}", isActive = true
        });
        await db.SaveChangesAsync();

        var populatedBadCursor = GroupImportAdapterProxy.Create(_ => Task.FromResult(new HostGroupPage(
            [new Delegat.Models.Marketplace.DTOs.ExternalGroup("g-1", "Summer", [])],
            true,
            null)));
        var groupSvc = new GroupSyncService(db, new SingleAdapterRegistry(populatedBadCursor), NullLogger<GroupSyncService>.Instance);
        var failedImport = await groupSvc.ImportGroupsAsync(10, 1, CancellationToken.None);
        Assert.False(failedImport.IsSuccess);
        Assert.Empty(await db.productGroups.AsNoTracking().ToListAsync());
    }
}
