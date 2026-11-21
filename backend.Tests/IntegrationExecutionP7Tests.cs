using System.Net;
using Delegat.Controllers.PublicApi;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.WooCommerce;
using Delegat.Models;
using Delegat.Models.Integration;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Integration;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class IntegrationExecutionP7Tests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubCurrentUser(int userId) : ICurrentUserService
    {
        public int? GetUserId() => userId;
        public Task<User?> GetUserAsync(CancellationToken ct = default) => Task.FromResult<User?>(null);
        public int? GetSelectedStoreId() => 1;
        public Task<int?> GetSelectedStoreIdAsync(CancellationToken ct = default) => Task.FromResult<int?>(1);
        public bool IsAuthenticated => true;
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

    private sealed class SingleAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
    {
        public IMarketplaceAdapter? GetByKey(string key) => adapter;
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public bool Exists(string key) => true;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
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

    private class OrderAdapterProxy : System.Reflection.DispatchProxy
    {
        public int LegacyCalls { get; private set; }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IMarketplaceAdapter.Capabilities)}")
                return Delegat.Services.Marketplace.MarketplaceCapabilities.UpdateOrderStatus;
            if (targetMethod?.Name == nameof(IMarketplaceAdapter.UpdateOrderStatusAsync))
            {
                LegacyCalls++;
                return Task.FromResult(new OrderStatusUpdateResult { Success = true });
            }
            if (targetMethod?.Name == nameof(IMarketplaceAdapter.GetOrderAsync))
                return Task.FromResult<ExternalOrder?>(null);
            throw new NotImplementedException(targetMethod?.Name);
        }

        public static (IMarketplaceAdapter Adapter, OrderAdapterProxy Proxy) Create()
        {
            var proxy = Create<IMarketplaceAdapter, OrderAdapterProxy>();
            return (proxy, (OrderAdapterProxy)(object)proxy);
        }
    }

    private static async Task SeedStoreMemberAndConnectionAsync(DataContext db, int userId = 7, int storeId = 1, int connId = 10)
    {
        var user = new User { Id = userId, UserName = "u@example.com", Email = "u@example.com", isActive = true };
        var store = new Store { Id = storeId, name = "Store 1", members = [user] };
        db.Users.Add(user);
        db.stores.Add(store);
        db.marketplaceConnections.Add(new MarketplaceConnection
        {
            Id = connId,
            storeId = storeId,
            name = "Woo Shop",
            adapterKey = "woocommerce",
            credentialsJson = """{"siteUrl":"https://woo.example.com","apiKey":"woo-k"}""",
            isActive = true
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PublicApi_OrderStatus_V1Admit_Returns202DeduplicatesKeyAndConflicts409()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        await SeedStoreMemberAndConnectionAsync(db);

        var (adapter, proxy) = OrderAdapterProxy.Create();
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new WooCommercePlugin(),
            new IntegrationExecutionOptions { Enabled = true }, commerce);

        var controller = new PublicMarketplaceOrdersController(
            new PublicApiStoreContext(new StubCurrentUser(7), db), commerce, exec)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "pub-ord-42";

        var first = await controller.UpdateOrderStatus(
            1, 10, "42", new OrderStatusUpdate { Action = "cancel", Reason = "Out of stock" }, CancellationToken.None);
        var accepted1 = Assert.IsType<AcceptedResult>(first);
        Assert.Equal(202, accepted1.StatusCode);
        Assert.Equal(0, proxy.LegacyCalls);

        // Retry with same Idempotency-Key and same body returns same operation without duplicating.
        var second = await controller.UpdateOrderStatus(
            1, 10, "42", new OrderStatusUpdate { Action = "cancel", Reason = "Out of stock" }, CancellationToken.None);
        var accepted2 = Assert.IsType<AcceptedResult>(second);
        Assert.Equal(accepted1.Location, accepted2.Location);
        Assert.Single(await db.integrationCommands.AsNoTracking().ToListAsync());

        // Same Idempotency-Key with different body returns 409 integration_request_conflict.
        var conflict = await Assert.ThrowsAsync<ApiException>(() => controller.UpdateOrderStatus(
            1, 10, "42", new OrderStatusUpdate { Action = "complete" }, CancellationToken.None));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("integration_request_conflict", conflict.Code);
    }

    [Fact]
    public async Task PublicApi_V1Disabled_PreservesLegacy200BehaviorAndAdmitsNothing()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        await SeedStoreMemberAndConnectionAsync(db);

        var (adapter, proxy) = OrderAdapterProxy.Create();
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new WooCommercePlugin(),
            new IntegrationExecutionOptions { Enabled = false }, commerce);

        var controller = new PublicMarketplaceOrdersController(
            new PublicApiStoreContext(new StubCurrentUser(7), db), commerce, exec)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var res = await controller.UpdateOrderStatus(
            1, 10, "42", new OrderStatusUpdate { Action = "complete" }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(res);
        Assert.Equal(1, proxy.LegacyCalls);
        Assert.Empty(await db.integrationCommands.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CallerDisconnect_DoesNotCancelAdmittedCommand_WhileUserStopAndRollbackDrainWorkCleanly()
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        var clock = new ManualClock();
        var woo = new WooCommercePlugin();
        var httpCalls = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOperationLogger, NoopOpLogger>();
        services.AddSingleton<IntegrationScopeAdmission>();
        services.AddSingleton<IntegrationHttpTransport>();
        services.AddScoped<IIntegrationExecutionBindingResolver>(_ => new StaticBindingResolver(
            new IntegrationExecutionBinding(woo, new ConnectionContext
            {
                ConnectionId = 10,
                AdapterKey = "woocommerce",
                Credentials = new Dictionary<string, string>
                {
                    ["siteUrl"] = "https://woo.example.com",
                    ["apiKey"] = "woo-k"
                }
            }, new IntegrationHostRouteBinding(
                IntegrationProviderKind.WooCommerce, IntegrationProviderEnvironment.Production,
                new Uri("https://woo.example.com"), VerifiedSellerId: "conn:10"))));
        services.AddScoped<IIntegrationStageDispatcher, IntegrationProviderStageDispatcher>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
            {
                httpCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true,"order":{"external_id":"42"}}""")
                });
            }));
        await using var provider = services.BuildServiceProvider();

        // Admit two commands while V1 is enabled.
        string cmd1OpId;
        string cmd2OpId;
        using (var callerCts = new CancellationTokenSource())
        await using (var db = TestDbFactory.CreateSqliteContext(dsn))
        {
            var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(OrderAdapterProxy.Create().Adapter), new MarketplaceOrderSnapshotService(db, clock));
            var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
            var exec = new MarketplaceCommerceExecutionService(
                coordinator, new IntegrationPreparationService(), _ => woo,
                new IntegrationExecutionOptions { Enabled = true }, commerce);

            var conn = new MarketplaceConnection
            {
                Id = 10, storeId = 1, name = "Woo", adapterKey = "woocommerce",
                credentialsJson = """{"siteUrl":"https://woo.example.com","apiKey":"woo-k"}""", isActive = true
            };

            var a1 = await exec.AdmitOrderActionAsync(conn, "42", new OrderStatusUpdate { Action = "complete" }, 7, null, "k-1", callerCts.Token);
            var a2 = await exec.AdmitOrderActionAsync(conn, "43", new OrderStatusUpdate { Action = "complete" }, 7, null, "k-2", callerCts.Token);
            cmd1OpId = a1.OperationId;
            cmd2OpId = a2.OperationId;

            // Simulate caller disconnect right after admission: does NOT cancel cmd1!
            callerCts.Cancel();

            // User explicitly clicks Stop on cmd2: DOES cancel cmd2 before any HTTP send!
            var stopped = await coordinator.RequestStopByOperationIdAsync(cmd2OpId, CancellationToken.None);
            Assert.True(stopped);
        }

        // Rollback check: disabling V1 blocks new admissions, while worker STILL drains cmd1 cleanly!
        await using (var db = TestDbFactory.CreateSqliteContext(dsn))
        {
            var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(OrderAdapterProxy.Create().Adapter), new MarketplaceOrderSnapshotService(db, clock));
            var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
            var execOff = new MarketplaceCommerceExecutionService(
                coordinator, new IntegrationPreparationService(), _ => woo,
                new IntegrationExecutionOptions { Enabled = false }, commerce);
            var conn = new MarketplaceConnection
            {
                Id = 10, storeId = 1, name = "Woo", adapterKey = "woocommerce",
                credentialsJson = """{"siteUrl":"https://woo.example.com","apiKey":"woo-k"}""", isActive = true
            };
            await Assert.ThrowsAsync<IntegrationExecutionDisabledException>(() =>
                execOff.AdmitOrderActionAsync(conn, "44", new OrderStatusUpdate { Action = "complete" }, 7, null, "k-3", CancellationToken.None));
        }

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        // Only cmd1 sent HTTP; cmd2 was cancelled by user Stop before send.
        Assert.Equal(1, httpCalls);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        var c1 = await verify.integrationCommands.AsNoTracking().SingleAsync(c => c.operationId == cmd1OpId);
        var c2 = await verify.integrationCommands.AsNoTracking().SingleAsync(c => c.operationId == cmd2OpId);
        Assert.Equal(IntegrationCommandState.Completed, c1.state);
        Assert.Equal(IntegrationCommandState.Cancelled, c2.state);
    }
}
