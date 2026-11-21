using System.Net;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.WooCommerce;
using Delegat.Models.Integration;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Integration;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class IntegrationExecutionP5Tests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }

    private sealed class StaticBindingResolver(IntegrationExecutionBinding? binding) : IIntegrationExecutionBindingResolver
    {
        public Task<IntegrationExecutionBinding?> ResolveAsync(DispatchStageRequest request, CancellationToken ct) =>
            Task.FromResult(binding);
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

    private sealed class EmptyAdapterRegistry : IMarketplaceAdapterRegistry
    {
        public IMarketplaceAdapter? GetByKey(string key) => null;
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [];
        public bool Exists(string key) => false;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private class StaleOrderReadAdapterProxy : System.Reflection.DispatchProxy
    {
        public ExternalOrder? StaleReadBack { get; set; }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IMarketplaceAdapter.GetOrderAsync))
                return Task.FromResult(StaleReadBack);
            throw new NotImplementedException(targetMethod?.Name);
        }

        public static IMarketplaceAdapter Create(ExternalOrder staleReadBack)
        {
            var proxy = Create<IMarketplaceAdapter, StaleOrderReadAdapterProxy>();
            ((StaleOrderReadAdapterProxy)(object)proxy).StaleReadBack = staleReadBack;
            return proxy;
        }
    }

    private static async Task<MarketplaceConnection> SeedStoreAndConnectionAsync(
        DataContext db, string adapterKey, string credentialsJson = "{}", int connectionId = 10)
    {
        if (!await db.stores.AnyAsync(s => s.Id == 1))
        {
            db.stores.Add(new Models.Store { Id = 1, name = "Test Store" });
        }
        var conn = new MarketplaceConnection
        {
            Id = connectionId,
            storeId = 1,
            name = $"{adapterKey}-shop",
            adapterKey = adapterKey,
            credentialsJson = credentialsJson,
            isActive = true
        };
        db.marketplaceConnections.Add(conn);
        await db.SaveChangesAsync();
        return conn;
    }

    private static MarketplaceConnection CreateConnection(string adapterKey, string credentialsJson = "{}") => new()
    {
        Id = 10,
        storeId = 1,
        name = $"{adapterKey}-shop",
        adapterKey = adapterKey,
        credentialsJson = credentialsJson,
        isActive = true
    };

    [Fact]
    public async Task OrderAction_ConfirmedApplied_AdvancesSnapshotAndHoldsAgainstStaleRefresh()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var snapshots = new MarketplaceOrderSnapshotService(db, clock);
        var commerce = new MarketplaceCommerceService(db, new EmptyAdapterRegistry(), snapshots);
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new OzonPlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);

        var conn = await SeedStoreAndConnectionAsync(db, "ozon");
        var initial = new ExternalOrder
        {
            ExternalId = "12345-0001-1",
            Status = MarketplaceOrderStatuses.AwaitingPack,
            Items = []
        };
        await snapshots.UpsertAsync(conn.storeId, conn.Id, initial, CancellationToken.None);

        // Marketplace read-back is still stale (AwaitingPack) immediately after confirm/pack.
        var adapter = StaleOrderReadAdapterProxy.Create(new ExternalOrder
        {
            ExternalId = "12345-0001-1",
            Status = MarketplaceOrderStatuses.AwaitingPack,
            Items = []
        });

        var projection = await exec.ApplyConfirmedOrderActionAsync(
            conn, adapter, "12345-0001-1", MarketplaceOrderActions.Pack,
            new IntegrationStageOutcome(IntegrationStageOutcomeKind.Applied), CancellationToken.None);

        Assert.Equal(OrderActionProjectionState.ConfirmedApplied, projection.State);
        Assert.True(projection.SnapshotAdvanced);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingDeliver, projection.Order?.Status);

        // A stale list refresh 30s later cannot revert the stored snapshot back to AwaitingPack.
        clock.Now = clock.Now.AddSeconds(30);
        await snapshots.UpsertAsync(conn.storeId, conn.Id, new ExternalOrder
        {
            ExternalId = "12345-0001-1",
            Status = MarketplaceOrderStatuses.AwaitingPack,
            Items = []
        }, CancellationToken.None);

        var stored = await snapshots.FindAsync(conn.storeId, conn.Id, "12345-0001-1", CancellationToken.None);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingDeliver, stored!.status);
    }

    [Theory]
    [InlineData(IntegrationStageOutcomeKind.Accepted, OrderActionProjectionState.PendingConfirmation)]
    [InlineData(IntegrationStageOutcomeKind.Pending, OrderActionProjectionState.PendingConfirmation)]
    [InlineData(IntegrationStageOutcomeKind.Unknown, OrderActionProjectionState.UnknownEffect)]
    [InlineData(IntegrationStageOutcomeKind.Rejected, OrderActionProjectionState.Rejected)]
    public async Task OrderAction_UnconfirmedOrUnknownOutcome_DoesNotAdvanceStoredSnapshot(
        IntegrationStageOutcomeKind outcomeKind,
        OrderActionProjectionState expectedState)
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var snapshots = new MarketplaceOrderSnapshotService(db, clock);
        var commerce = new MarketplaceCommerceService(db, new EmptyAdapterRegistry(), snapshots);
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new OzonPlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);

        var conn = await SeedStoreAndConnectionAsync(db, "ozon");
        await snapshots.UpsertAsync(conn.storeId, conn.Id, new ExternalOrder
        {
            ExternalId = "ORD-99",
            Status = MarketplaceOrderStatuses.AwaitingPack,
            Items = []
        }, CancellationToken.None);

        var adapter = StaleOrderReadAdapterProxy.Create(new ExternalOrder
        {
            ExternalId = "ORD-99",
            Status = MarketplaceOrderStatuses.AwaitingPack,
            Items = []
        });

        var projection = await exec.ApplyConfirmedOrderActionAsync(
            conn, adapter, "ORD-99", MarketplaceOrderActions.Pack,
            new IntegrationStageOutcome(outcomeKind), CancellationToken.None);

        Assert.Equal(expectedState, projection.State);
        Assert.False(projection.SnapshotAdvanced);

        var stored = await snapshots.FindAsync(conn.storeId, conn.Id, "ORD-99", CancellationToken.None);
        Assert.Equal(MarketplaceOrderStatuses.AwaitingPack, stored!.status);
    }

    [Fact]
    public async Task OrderAction_V1Admit_DeduplicatesIdempotencyKey_AndConflictsOnDifferentAction()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var snapshots = new MarketplaceOrderSnapshotService(db, clock);
        var commerce = new MarketplaceCommerceService(db, new EmptyAdapterRegistry(), snapshots);
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new WooCommercePlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);

        var conn = CreateConnection("woocommerce", """{"siteUrl":"https://woo.example","apiKey":"k1"}""");

        var first = await exec.AdmitOrderActionAsync(
            conn, "42", new OrderStatusUpdate { Action = "cancel", Reason = "Out of stock" },
            authorUserId: 7, authorApiKeyId: null, requestKey: "req-ord-42", CancellationToken.None);
        var second = await exec.AdmitOrderActionAsync(
            conn, "42", new OrderStatusUpdate { Action = "cancel", Reason = "Out of stock" },
            authorUserId: 7, authorApiKeyId: null, requestKey: "req-ord-42", CancellationToken.None);

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.CommandId, second.CommandId);

        var conflict = await Assert.ThrowsAsync<ApiException>(() => exec.AdmitOrderActionAsync(
            conn, "42", new OrderStatusUpdate { Action = "complete" },
            authorUserId: 7, authorApiKeyId: null, requestKey: "req-ord-42", CancellationToken.None));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("integration_request_conflict", conflict.Code);
    }

    [Fact]
    public async Task Shipment_UnsupportedWildberriesBoxAssign_BlockedAtPreflightBeforeAnyAdmission()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var snapshots = new MarketplaceOrderSnapshotService(db, clock);
        var commerce = new MarketplaceCommerceService(db, new EmptyAdapterRegistry(), snapshots);
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new WildberriesPlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);

        var conn = CreateConnection("wildberries", """{"apiKey":"wb-key"}""");

        var ex = await Assert.ThrowsAsync<ApiException>(() => exec.AdmitShipmentActionAsync(
            conn, "trbx_assign", "WB-GI-1:WB-TRBX-1",
            new Dictionary<string, object?>
            {
                ["supplyId"] = "WB-GI-1",
                ["trbxId"] = "WB-TRBX-1",
                ["orderIds"] = new[] { "101" }
            },
            "Assign orders to box", 7, null, "req-wb-box-1", CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Empty(await db.integrationCommands.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Shipment_SupplyCreatedThenDeliverFails_RecoversDeliverIndependentlyWithoutRepeatingCreate()
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        var clock = new ManualClock();
        var wb = new WildberriesPlugin();
        var createCalls = 0;
        var deliverCalls = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOperationLogger, NoopOpLogger>();
        services.AddSingleton<IntegrationScopeAdmission>();
        services.AddSingleton<IntegrationHttpTransport>();
        services.AddScoped<IIntegrationExecutionBindingResolver>(_ => new StaticBindingResolver(
            new IntegrationExecutionBinding(wb, new ConnectionContext
            {
                ConnectionId = 10,
                AdapterKey = "wildberries",
                Credentials = new Dictionary<string, string> { ["apiKey"] = "wb-key" }
            }, new IntegrationHostRouteBinding(
                IntegrationProviderKind.Wildberries, IntegrationProviderEnvironment.Production,
                new Uri("https://marketplace-api.wildberries.ru"), VerifiedSellerId: "wb-acct-1"))));
        services.AddScoped<IIntegrationStageDispatcher, IntegrationProviderStageDispatcher>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((req, _) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path.EndsWith("/api/v3/supplies", StringComparison.Ordinal))
                {
                    createCalls++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent("""{"id":"WB-GI-500"}""")
                    });
                }
                if (path.Contains("/api/v3/supplies/WB-GI-500/deliver", StringComparison.Ordinal))
                {
                    deliverCalls++;
                    if (deliverCalls == 1)
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new StringContent("""{"error":"temp"}""")
                        });
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }));
        await using var provider = services.BuildServiceProvider();

        // Command 1: Create supply -> Applied.
        await using (var db = TestDbFactory.CreateSqliteContext(dsn))
        {
            var snapshots = new MarketplaceOrderSnapshotService(db, clock);
            var commerce = new MarketplaceCommerceService(db, new EmptyAdapterRegistry(), snapshots);
            var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
            var exec = new MarketplaceCommerceExecutionService(
                coordinator, new IntegrationPreparationService(), _ => wb,
                new IntegrationExecutionOptions { Enabled = true }, commerce);

            var conn = CreateConnection("wildberries", """{"apiKey":"wb-key"}""");
            await exec.AdmitShipmentActionAsync(
                conn, "create_supply", "supply-1", new Dictionary<string, object?> { ["name"] = "Supply 1" },
                "Create supply", 7, null, "req-supply-create", CancellationToken.None);
        }

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, createCalls);

        // Command 2: Deliver WB-GI-500 -> fails with 503, then recovers without repeating supply create!
        await using (var db = TestDbFactory.CreateSqliteContext(dsn))
        {
            var snapshots = new MarketplaceOrderSnapshotService(db, clock);
            var commerce = new MarketplaceCommerceService(db, new EmptyAdapterRegistry(), snapshots);
            var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
            var exec = new MarketplaceCommerceExecutionService(
                coordinator, new IntegrationPreparationService(), _ => wb,
                new IntegrationExecutionOptions { Enabled = true }, commerce);

            var conn = CreateConnection("wildberries", """{"apiKey":"wb-key"}""");
            await exec.AdmitShipmentActionAsync(
                conn, "deliver_supply", "WB-GI-500",
                new Dictionary<string, object?> { ["supplyId"] = "WB-GI-500" },
                "Deliver supply", 7, null, "req-supply-deliver", CancellationToken.None);
        }

        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, deliverCalls);
        Assert.Equal(1, createCalls);

        clock.Now = clock.Now.AddMinutes(2);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(2, deliverCalls);
        Assert.Equal(1, createCalls);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        var cmds = await verify.integrationCommands.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(2, cmds.Count);
        Assert.All(cmds, c => Assert.Equal(IntegrationCommandState.Completed, c.state));
    }

    [Fact]
    public async Task OrderLabel_OzonAsyncLabelCreateAndConfirm_CompletesThroughWorker()
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        var clock = new ManualClock();
        var ozon = new OzonPlugin();
        var createCalls = 0;
        var getCalls = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOperationLogger, NoopOpLogger>();
        services.AddSingleton<IntegrationScopeAdmission>();
        services.AddSingleton<IntegrationHttpTransport>();
        services.AddScoped<IIntegrationExecutionBindingResolver>(_ => new StaticBindingResolver(
            new IntegrationExecutionBinding(ozon, new ConnectionContext
            {
                ConnectionId = 11,
                AdapterKey = "ozon",
                Credentials = new Dictionary<string, string> { ["clientId"] = "12345", ["apiKey"] = "oz-key" }
            }, new IntegrationHostRouteBinding(
                IntegrationProviderKind.Ozon, IntegrationProviderEnvironment.Production,
                new Uri("https://api-seller.ozon.ru"), VerifiedSellerId: "oz-acct-1"))));
        services.AddScoped<IIntegrationStageDispatcher, IntegrationProviderStageDispatcher>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((req, _) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path == "/v3/posting/fbs/package-label/create")
                {
                    createCalls++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"tasks":[{"task_id":5501,"task_type":"big_label"}]}""")
                    });
                }
                if (path == "/v2/posting/fbs/package-label/get")
                {
                    getCalls++;
                    var body = getCalls == 1
                        ? """{"status":{"code":"in_progress"}}"""
                        : """{"status":{"code":"completed"},"file_url":"https://cdn.ozon.example/5501.pdf"}""";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(body)
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }));
        await using var provider = services.BuildServiceProvider();

        await using (var db = TestDbFactory.CreateSqliteContext(dsn))
        {
            var snapshots = new MarketplaceOrderSnapshotService(db, clock);
            var commerce = new MarketplaceCommerceService(db, new EmptyAdapterRegistry(), snapshots);
            var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
            var exec = new MarketplaceCommerceExecutionService(
                coordinator, new IntegrationPreparationService(), _ => ozon,
                new IntegrationExecutionOptions { Enabled = true }, commerce);

            var conn = CreateConnection("ozon", """{"clientId":"12345","apiKey":"oz-key"}""");
            await exec.AdmitShipmentActionAsync(
                conn, "label", "12345-0001-1",
                new Dictionary<string, object?> { ["postingNumber"] = "12345-0001-1" },
                "Create shipping label", 7, null, "req-label-1", CancellationToken.None);
        }

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);

        // Poll 1: create -> Accepted.
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        // Poll 2: confirm -> Pending (in_progress).
        clock.Now = clock.Now.AddSeconds(20);
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        // Poll 3: confirm -> Applied (completed).
        clock.Now = clock.Now.AddSeconds(20);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(1, createCalls);
        Assert.Equal(2, getCalls);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Equal(IntegrationCommandState.Completed, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }

    [Fact]
    public async Task OrderAction_V1Disabled_ThrowsDisabledAndAdmitsNothing()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var clock = new ManualClock();
        var snapshots = new MarketplaceOrderSnapshotService(db, clock);
        var commerce = new MarketplaceCommerceService(db, new EmptyAdapterRegistry(), snapshots);
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new WooCommercePlugin(),
            new IntegrationExecutionOptions { Enabled = false }, commerce);

        var conn = CreateConnection("woocommerce", """{"siteUrl":"https://woo.example","apiKey":"k1"}""");

        await Assert.ThrowsAsync<IntegrationExecutionDisabledException>(() => exec.AdmitOrderActionAsync(
            conn, "42", new OrderStatusUpdate { Action = "complete" },
            7, null, "req-disabled", CancellationToken.None));
        Assert.Empty(await db.integrationCommands.AsNoTracking().ToListAsync());
    }
}
