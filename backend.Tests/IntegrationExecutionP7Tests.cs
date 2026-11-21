using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Delegat.Controllers;
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
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 0 }, commerce);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("apiKeyId", "99")], "ApiKey"))
        };
        var controller = new PublicMarketplaceOrdersController(
            new PublicApiStoreContext(new StubCurrentUser(7), db), commerce, exec)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "pub-ord-42";

        var first = await controller.UpdateOrderStatus(
            1, 10, "42", new OrderStatusUpdate
            {
                Action = "cancel",
                Reason = "Out of stock",
                Extra = new Dictionary<string, string> { ["supplyId"] = "WB-1", ["boxId"] = "B-2" }
            }, CancellationToken.None);
        var accepted1 = Assert.IsType<AcceptedResult>(first);
        Assert.Equal(202, accepted1.StatusCode);
        Assert.Equal(0, proxy.LegacyCalls);

        // Verify API key attribution was persisted on both command and operation.
        var persistedCmd = await db.integrationCommands.AsNoTracking().SingleAsync();
        var persistedOp = await db.operations.AsNoTracking().SingleAsync();
        Assert.Equal(99, persistedCmd.authorApiKeyId);
        Assert.Equal(99, persistedOp.authorApiKeyId);

        // Retry with same Idempotency-Key and reordered Extra properties returns same operation without 409.
        var second = await controller.UpdateOrderStatus(
            1, 10, "42", new OrderStatusUpdate
            {
                Action = "cancel",
                Reason = "Out of stock",
                Extra = new Dictionary<string, string> { ["boxId"] = "B-2", ["supplyId"] = "WB-1" }
            }, CancellationToken.None);
        var accepted2 = Assert.IsType<AcceptedResult>(second);
        Assert.Equal(accepted1.Location, accepted2.Location);
        Assert.Single(await db.integrationCommands.AsNoTracking().ToListAsync());

        // Same Idempotency-Key with a changed Extra value returns 409 integration_request_conflict.
        var extraConflict = await Assert.ThrowsAsync<ApiException>(() => controller.UpdateOrderStatus(
            1, 10, "42", new OrderStatusUpdate
            {
                Action = "cancel",
                Reason = "Out of stock",
                Extra = new Dictionary<string, string> { ["boxId"] = "B-999", ["supplyId"] = "WB-1" }
            }, CancellationToken.None));
        Assert.Equal(409, extraConflict.StatusCode);
        Assert.Equal("integration_request_conflict", extraConflict.Code);

        // Same Idempotency-Key with different action returns 409 integration_request_conflict.
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

    private sealed class FakeShipmentExecutionProvider : IMarketplaceExecutionProvider
    {
        public Task<IntegrationPreparationResult> PrepareAsync(
            IntegrationPreparationInput input, CancellationToken ct = default)
        {
            var items = input.Items.Select(item =>
            {
                var action = item.Data.TryGetValue("action", out var a) ? a?.ToString() ?? "act" : "act";
                return new IntegrationPreparedItem(item.ItemKey,
                [
                    new IntegrationPreparedStage(
                        $"shipment:{action}",
                        IntegrationStageKind.ShipmentAction,
                        $"{input.CommandId}:{item.ItemKey}:{action}",
                        JsonSerializer.Serialize(item.Data),
                        [],
                        IsEffectful: true)
                ]);
            }).ToList();

            return Task.FromResult(new IntegrationPreparationResult(items, []));
        }

        public Task<IntegrationStageOutcome> ExecuteStageAsync(
            IntegrationStageRequest request, CancellationToken ct = default) =>
            Task.FromResult(new IntegrationStageOutcome(IntegrationStageOutcomeKind.Applied));

        public Task<IntegrationStageOutcome> ConfirmAsync(
            IntegrationConfirmationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new IntegrationStageOutcome(IntegrationStageOutcomeKind.Applied));

        public Task<IntegrationStageOutcome> CancelAcceptedAsync(
            IntegrationConfirmationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new IntegrationStageOutcome(IntegrationStageOutcomeKind.Cancelled));
    }

    private class ShipmentAdapterProxy : System.Reflection.DispatchProxy
    {
        public int LegacyCreateCalls { get; private set; }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == $"get_{nameof(IMarketplaceAdapter.Capabilities)}")
                return Delegat.Services.Marketplace.MarketplaceCapabilities.ManageShipments;
            if (targetMethod?.Name == nameof(IMarketplaceAdapter.CreateShipmentAsync))
            {
                LegacyCreateCalls++;
                return Task.FromResult(new ShipmentMutationResult { Success = true });
            }
            throw new NotImplementedException(targetMethod?.Name);
        }

        public static (IMarketplaceAdapter Adapter, ShipmentAdapterProxy Proxy) Create()
        {
            var proxy = Create<IMarketplaceAdapter, ShipmentAdapterProxy>();
            return (proxy, (ShipmentAdapterProxy)(object)proxy);
        }
    }

    [Fact]
    public async Task PublicApi_CreateShipment_AlwaysAdmitsWhenV1Enabled_WithAndWithoutPreferAsync()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        await SeedStoreMemberAndConnectionAsync(db);

        var (adapter, proxy) = ShipmentAdapterProxy.Create();
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new FakeShipmentExecutionProvider(),
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 0 }, commerce);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("apiKeyId", "55")], "ApiKey"))
        };
        httpContext.Request.Headers["Idempotency-Key"] = "ship-create-1";

        var controller = new PublicMarketplaceShipmentsController(
            new PublicApiStoreContext(new StubCurrentUser(7), db), commerce, exec)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        // Without Prefer: respond-async, V1 still admits through the coordinator (no legacy bypass).
        var first = await controller.CreateShipment(1, 10, new CreateShipmentRequest { Name = "Supply A" }, CancellationToken.None);
        var accepted1 = Assert.IsType<AcceptedResult>(first);
        Assert.Equal(0, proxy.LegacyCreateCalls);

        var cmd = await db.integrationCommands.AsNoTracking().SingleAsync();
        Assert.Equal(55, cmd.authorApiKeyId);

        // Keyed retry without Prefer reuses the original command.
        var retrySync = await controller.CreateShipment(1, 10, new CreateShipmentRequest { Name = "Supply A" }, CancellationToken.None);
        var accepted2 = Assert.IsType<AcceptedResult>(retrySync);
        Assert.Equal(accepted1.Location, accepted2.Location);

        // Keyed retry with Prefer: respond-async also reuses the exact same command.
        controller.HttpContext.Request.Headers["Prefer"] = "respond-async";
        var retryAsync = await controller.CreateShipment(1, 10, new CreateShipmentRequest { Name = "Supply A" }, CancellationToken.None);
        var accepted3 = Assert.IsType<AcceptedResult>(retryAsync);
        Assert.Equal(accepted1.Location, accepted3.Location);
        Assert.Single(await db.integrationCommands.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PublicApi_CreateShipmentBoxes_ExposesBoxIdsOnPollingAndReturnsDtoWithinBoundedWait()
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await SeedStoreMemberAndConnectionAsync(schemaDb);

        var (adapter, _) = ShipmentAdapterProxy.Create();
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(schemaDb, new SingleAdapterRegistry(adapter), new MarketplaceOrderSnapshotService(schemaDb, clock));
        var coordinator = new IntegrationCoordinator(schemaDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new FakeShipmentExecutionProvider(),
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 5000, PublicApiSyncPollIntervalMs = 20 }, commerce);

        var storeCtx = new PublicApiStoreContext(new StubCurrentUser(7), schemaDb);
        var shipmentsController = new PublicMarketplaceShipmentsController(storeCtx, commerce, exec)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        shipmentsController.HttpContext.Request.Headers["Prefer"] = "respond-async";
        shipmentsController.HttpContext.Request.Headers["Idempotency-Key"] = "boxes-async-1";

        var asyncRes = await shipmentsController.CreateShipmentBoxes(
            1, 10, "WB-GI-10", new CreateShipmentBoxesRequest { Amount = 2 }, CancellationToken.None);
        var accepted = Assert.IsType<AcceptedResult>(asyncRes);
        var acceptedJson = JsonSerializer.Serialize(accepted.Value);
        using var acceptedDoc = JsonDocument.Parse(acceptedJson);
        var operationId = acceptedDoc.RootElement.GetProperty("operationId").GetString()!;

        // Simulate worker completing the stage with created box IDs persisted in evidenceJson.
        var cmd = await schemaDb.integrationCommands.SingleAsync(c => c.operationId == operationId);
        var stage = await schemaDb.integrationStages.SingleAsync(s => s.commandId == cmd.Id);
        stage.state = IntegrationStageState.Applied;
        stage.evidenceJson = """{"boxIds":["WB-TRBX-1","WB-TRBX-2"]}""";
        cmd.state = IntegrationCommandState.Completed;
        cmd.finishedAt = DateTime.UtcNow;
        var op = await schemaDb.operations.SingleAsync(o => o.operationId == operationId);
        op.status = OperationLifecycle.Completed;
        await schemaDb.SaveChangesAsync();

        // Poll store-scoped GetOperation and verify box IDs are surfaced for follow-up assignment.
        var connectionsController = new PublicConnectionsController(storeCtx, schemaDb, new SingleAdapterRegistry(adapter));
        var pollRes = await connectionsController.GetOperation(1, operationId, CancellationToken.None);
        var pollOk = Assert.IsType<OkObjectResult>(pollRes);
        using var pollDoc = JsonDocument.Parse(JsonSerializer.Serialize(pollOk.Value));
        var integration = pollDoc.RootElement.GetProperty("integration");
        Assert.Equal("completed", integration.GetProperty("state").GetString());
        var boxIds = integration.GetProperty("result").GetProperty("boxIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["WB-TRBX-1", "WB-TRBX-2"], boxIds);

        // Follow-up assignment using the discovered box ID succeeds through the coordinator.
        shipmentsController.HttpContext.Request.Headers["Idempotency-Key"] = "boxes-assign-1";
        var assignRes = await shipmentsController.AssignShipmentBoxOrders(
            1, 10, "WB-GI-10", boxIds[0]!, new ShipmentOrdersUpdate { OrderIds = ["1001"] }, CancellationToken.None);
        Assert.IsType<AcceptedResult>(assignRes);

        // Without Prefer: respond-async, when the command completes during the bounded wait, return the typed DTO (200 OK).
        shipmentsController.HttpContext.Request.Headers.Remove("Prefer");
        shipmentsController.HttpContext.Request.Headers["Idempotency-Key"] = "boxes-sync-1";

        var completer = Task.Run(async () =>
        {
            for (var i = 0; i < 120; i++)
            {
                await Task.Delay(25);
                await using var bgDb = TestDbFactory.CreateSqliteContext(dsn);
                var bgCmd = await bgDb.integrationCommands.FirstOrDefaultAsync(c => c.requestKey == "boxes-sync-1");
                if (bgCmd == null) continue;
                await bgDb.integrationStages
                    .Where(s => s.commandId == bgCmd.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.state, IntegrationStageState.Applied)
                        .SetProperty(x => x.evidenceJson, """{"boxIds":["WB-TRBX-9"]}"""));
                await bgDb.integrationCommands
                    .Where(c => c.Id == bgCmd.Id)
                    .ExecuteUpdateAsync(c => c
                        .SetProperty(x => x.state, IntegrationCommandState.Completed)
                        .SetProperty(x => x.finishedAt, (DateTime?)DateTime.UtcNow));
                return;
            }
        });

        var syncRes = await shipmentsController.CreateShipmentBoxes(
            1, 10, "WB-GI-10", new CreateShipmentBoxesRequest { Amount = 1 }, CancellationToken.None);
        await completer;

        var okDto = Assert.IsType<OkObjectResult>(syncRes);
        var boxesDto = Assert.IsType<CreateShipmentBoxesResult>(okDto.Value);
        Assert.True(boxesDto.Success);
        var onlyBox = Assert.Single(boxesDto.Boxes);
        Assert.Equal("WB-TRBX-9", onlyBox.ExternalId);
    }

    private sealed class ConfigurableDispatcher(
        IntegrationDispatchResult dispatchResult,
        IntegrationDispatchResult confirmResult) : IIntegrationStageDispatcher
    {
        public Task<IntegrationDispatchResult> DispatchAsync(DispatchStageRequest request, CancellationToken ct) =>
            Task.FromResult(dispatchResult);

        public Task<IntegrationDispatchResult> ConfirmAsync(DispatchStageRequest request, CancellationToken ct) =>
            Task.FromResult(confirmResult);
    }

    [Theory]
    [InlineData(true, "partial", "partial")]
    [InlineData(false, "failed", "unknown")]
    public async Task OperationsStop_AfterSendStarted_PreservesAuthoritativeOutcomeOnPublicPoll(
        bool confirmApplied,
        string expectedOperationStatus,
        string expectedIntegrationState)
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await SeedStoreMemberAndConnectionAsync(schemaDb);

        var clock = new ManualClock();
        var confirmResult = confirmApplied
            ? new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied, EvidenceJson: """{"confirmed":true}""")
            : new IntegrationDispatchResult(IntegrationDispatchOutcome.Unknown, ResponseSummary: "ambiguous_timeout");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOperationLogger, NoopOpLogger>();
        services.AddSingleton<IIntegrationStageDispatcher>(new ConfigurableDispatcher(
            new IntegrationDispatchResult(IntegrationDispatchOutcome.Accepted, NextCheckAt: clock.GetUtcNow().UtcDateTime.AddSeconds(1)),
            confirmResult));
        await using var provider = services.BuildServiceProvider();

        var coordinator = new IntegrationCoordinator(schemaDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var admitted = await coordinator.AdmitAsync(new IntegrationAdmitRequest(
            Kind: "order_action",
            StoreId: 1,
            AuthorUserId: 7,
            AuthorApiKeyId: 12,
            ConnectionId: 10,
            RequestKey: $"stop-after-send-{confirmApplied}",
            InputJson: """{"orderId":"42"}""",
            Title: "Order 42",
            Stages: [new IntegrationStageSeed("42", "order:status", "order_action", 0, "{}", null, [], null, true)]),
            v1Enabled: true,
            CancellationToken.None);

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);

        // First worker tick dispatches the write and leaves it Accepted (in-flight confirmation).
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(IntegrationStageState.Accepted, (await schemaDb.integrationStages.AsNoTracking().SingleAsync()).state);

        // Operator clicks Stop via OperationsController AFTER send has started.
        var opsController = new OperationsController(
            schemaDb, new StubCurrentUser(7), new NoopOpLogger(),
            storageService: null!, coordinator);
        var stopRes = await opsController.StopOperation(admitted.OperationId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(stopRes);

        // Operation must NOT be prematurely marked terminal cancelled before sent stage reconciles.
        await using (var midDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var midOp = await midDb.operations.AsNoTracking().SingleAsync(o => o.operationId == admitted.OperationId);
            Assert.Equal(OperationLifecycle.Running, midOp.status);
        }

        // Advance clock so confirmation runs and reconciles the sent stage.
        clock.Now = clock.Now.AddSeconds(10);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        // Public API polling route exposes the authoritative outcome, never a stale cancelled status.
        await using var pollDb = TestDbFactory.CreateSqliteContext(dsn);
        var connectionsController = new PublicConnectionsController(
            new PublicApiStoreContext(new StubCurrentUser(7), pollDb),
            pollDb,
            new SingleAdapterRegistry(OrderAdapterProxy.Create().Adapter));
        var pollRes = await connectionsController.GetOperation(1, admitted.OperationId, CancellationToken.None);
        var pollOk = Assert.IsType<OkObjectResult>(pollRes);
        using var pollDoc = JsonDocument.Parse(JsonSerializer.Serialize(pollOk.Value));
        Assert.Equal(expectedOperationStatus, pollDoc.RootElement.GetProperty("status").GetString());
        Assert.Equal(expectedIntegrationState, pollDoc.RootElement.GetProperty("integration").GetProperty("state").GetString());
    }

    [Fact]
    public async Task PublicApi_OrderStatus_CompletedDuringWait_ProjectsSnapshotAndReturnsUpdatedOrder()
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await SeedStoreMemberAndConnectionAsync(schemaDb);

        var clock = new ManualClock();
        var snapshots = new MarketplaceOrderSnapshotService(schemaDb, clock);
        await snapshots.UpsertAsync(1, 10, new ExternalOrder
        {
            ExternalId = "42",
            Status = "new",
            CreatedAt = clock.GetUtcNow().UtcDateTime
        }, CancellationToken.None);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOperationLogger, NoopOpLogger>();
        services.AddSingleton<IIntegrationStageDispatcher>(new ConfigurableDispatcher(
            new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied, EvidenceJson: """{"status":200}"""),
            new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied)));
        await using var provider = services.BuildServiceProvider();

        var (adapter, _) = OrderAdapterProxy.Create();
        var commerce = new MarketplaceCommerceService(schemaDb, new SingleAdapterRegistry(adapter), snapshots);
        var coordinator = new IntegrationCoordinator(schemaDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new WooCommercePlugin(),
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 5000, PublicApiSyncPollIntervalMs = 20 }, commerce);

        var controller = new PublicMarketplaceOrdersController(
            new PublicApiStoreContext(new StubCurrentUser(7), schemaDb), commerce, exec)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "ord-snap-42";

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);
        var runner = Task.Run(async () =>
        {
            for (var i = 0; i < 120; i++)
            {
                await Task.Delay(25);
                await worker.ProcessDueStagesAsync(CancellationToken.None);
                await using var checkDb = TestDbFactory.CreateSqliteContext(dsn);
                var done = await checkDb.integrationCommands.AnyAsync(c => c.requestKey == "ord-snap-42" && c.state == IntegrationCommandState.Completed);
                if (done) return;
            }
        });

        var res = await controller.UpdateOrderStatus(
            1, 10, "42", new OrderStatusUpdate { Action = "cancel", Reason = "Out of stock" }, CancellationToken.None);
        await runner;

        var ok = Assert.IsType<OkObjectResult>(res);
        var dto = Assert.IsType<OrderStatusUpdateResult>(ok.Value);
        Assert.True(dto.Success);
        Assert.NotNull(dto.Order);
        Assert.Equal("cancelled", dto.Order!.Status);

        // Subsequent snapshot read also sees the projected status.
        await using var verifyDb = TestDbFactory.CreateSqliteContext(dsn);
        var verifySnapshots = new MarketplaceOrderSnapshotService(verifyDb, clock);
        var stored = await verifySnapshots.FindAsync(1, 10, "42", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("cancelled", stored!.status);
    }

    [Fact]
    public void IntegrationCommandResultExtractor_SingleStagePromotionUpdate_DerivesCountsFromPayload()
    {
        var cmd = new IntegrationCommand
        {
            commandId = "cmd-promo",
            operationId = "op-promo",
            kind = "promotion_action",
            state = IntegrationCommandState.Completed
        };

        var stages = new List<IntegrationStage>
        {
            new()
            {
                itemKey = "promo-1",
                stageKey = "promo:update",
                kind = "promotion_action",
                state = IntegrationStageState.Applied,
                payloadJson = """{"add":[{"externalProductId":"101"},{"externalProductId":"102"},{"externalProductId":"103"}],"removeExternalProductIds":["202"]}"""
            },
            new()
            {
                itemKey = "promo-2",
                stageKey = "promo:explicit",
                kind = "promotion_action",
                state = IntegrationStageState.Applied,
                evidenceJson = """{"added":2,"removed":0}"""
            },
            new()
            {
                itemKey = "promo-3",
                stageKey = "promo:remove",
                kind = "promotion_action",
                state = IntegrationStageState.Applied,
                payloadJson = """{"action":"deactivate","productId":"303"}"""
            }
        };

        var result = IntegrationCommandResultExtractor.BuildPromotionProductsResult(cmd, stages);
        Assert.True(result.Success);
        Assert.Equal(5, result.Added);
        Assert.Equal(2, result.Removed);
    }

    [Fact]
    public async Task IntegrationCoordinator_LegacyRawHashRow_MatchesEquivalentCanonicalRetry()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;

        var rawInput = """{"connectionId":10,"adapterKey":"woocommerce","kind":"order_action","itemKey":"42","data":{"orderId":"42","action":"cancel"}}""";
        var rawHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawInput))).ToLowerInvariant();
        Assert.NotEqual(rawHash, IntegrationCoordinator.ComputeHash(rawInput));

        db.integrationCommands.Add(new IntegrationCommand
        {
            commandId = "legacy-cmd-1",
            operationId = "legacy-op-1",
            storeId = 1,
            authorUserId = 7,
            connectionId = 10,
            scopeKey = "1:10:order_action",
            kind = "order_action",
            requestKey = "legacy-key-1",
            requestHash = rawHash,
            inputJson = rawInput,
            state = IntegrationCommandState.Admitted
        });
        await db.SaveChangesAsync();

        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);

        // Equivalent retry with reordered keys matches the legacy raw-hash row cleanly.
        var reorderedInput = """{"adapterKey":"woocommerce","connectionId":10,"data":{"action":"cancel","orderId":"42"},"itemKey":"42","kind":"order_action"}""";
        var dup = await coordinator.AdmitAsync(new IntegrationAdmitRequest(
            "order_action", 1, 7, null, 10, "legacy-key-1", reorderedInput, "Order 42",
            [new IntegrationStageSeed("42", "order:cancel", "order_action", 0)]), v1Enabled: true, CancellationToken.None);
        Assert.True(dup.IsDuplicate);
        Assert.Equal("legacy-cmd-1", dup.CommandId);

        // Different body with same key still throws conflict.
        var differentInput = """{"adapterKey":"woocommerce","connectionId":10,"data":{"action":"complete","orderId":"42"},"itemKey":"42","kind":"order_action"}""";
        await Assert.ThrowsAsync<IntegrationRequestConflictException>(() => coordinator.AdmitAsync(new IntegrationAdmitRequest(
            "order_action", 1, 7, null, 10, "legacy-key-1", differentInput, "Order 42",
            [new IntegrationStageSeed("42", "order:complete", "order_action", 0)]), v1Enabled: true, CancellationToken.None));
    }

    [Fact]
    public async Task PublicApi_OrderStatus_ConflictingExtraOrderIdRejected_AndProjectionUsesItemKey()
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await SeedStoreMemberAndConnectionAsync(schemaDb);

        var clock = new ManualClock();
        var snapshots = new MarketplaceOrderSnapshotService(schemaDb, clock);
        await snapshots.UpsertAsync(1, 10, new ExternalOrder { ExternalId = "A", Status = "new", CreatedAt = clock.GetUtcNow().UtcDateTime }, CancellationToken.None);
        await snapshots.UpsertAsync(1, 10, new ExternalOrder { ExternalId = "B", Status = "new", CreatedAt = clock.GetUtcNow().UtcDateTime }, CancellationToken.None);

        var (adapter, _) = OrderAdapterProxy.Create();
        var commerce = new MarketplaceCommerceService(schemaDb, new SingleAdapterRegistry(adapter), snapshots);
        var coordinator = new IntegrationCoordinator(schemaDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new WooCommercePlugin(),
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 0 }, commerce);

        var conn = await schemaDb.marketplaceConnections.SingleAsync(c => c.Id == 10);

        // Conflicting extra.orderId is rejected at admission before any command is created.
        var ex = await Assert.ThrowsAsync<ApiException>(() => exec.AdmitOrderActionAsync(
            conn, "A", new OrderStatusUpdate
            {
                Action = "cancel",
                Extra = new Dictionary<string, string> { ["orderId"] = "B" }
            }, 7, null, "conflict-extra-1", CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("order_status_failed", ex.Code);

        // Even if a pre-existing command row had data.orderId != itemKey, worker projects itemKey ("A") and leaves "B" untouched.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOperationLogger, NoopOpLogger>();
        services.AddSingleton<IIntegrationStageDispatcher>(new ConfigurableDispatcher(
            new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied, EvidenceJson: """{"status":200}"""),
            new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied)));
        await using var provider = services.BuildServiceProvider();

        await coordinator.AdmitAsync(new IntegrationAdmitRequest(
            "order_action", 1, 7, null, 10, "itemkey-priority-1",
            """{"adapterKey":"woocommerce","itemKey":"A","data":{"orderId":"B","action":"cancel"}}""",
            "Cancel A",
            [new IntegrationStageSeed("A", "order:cancel", "order_action", 0)]),
            v1Enabled: true, CancellationToken.None);

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        await using var verifyDb = TestDbFactory.CreateSqliteContext(dsn);
        var verifySnapshots = new MarketplaceOrderSnapshotService(verifyDb, clock);
        Assert.Equal("cancelled", (await verifySnapshots.FindAsync(1, 10, "A", CancellationToken.None))!.status);
        Assert.Equal("new", (await verifySnapshots.FindAsync(1, 10, "B", CancellationToken.None))!.status);
    }

    [Fact]
    public async Task UpdateStatusProjectionAsync_RewritesStatusAndPayloadAfterRollbackOnSameContext()
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await SeedStoreMemberAndConnectionAsync(schemaDb);

        var clock = new ManualClock();
        var snapshots = new MarketplaceOrderSnapshotService(schemaDb, clock);
        await snapshots.UpsertAsync(1, 10, new ExternalOrder
        {
            ExternalId = "42",
            Status = "new",
            CreatedAt = clock.GetUtcNow().UtcDateTime
        }, CancellationToken.None);

        var row = (await snapshots.FindAsync(1, 10, "42", CancellationToken.None))!;
        var live = snapshots.ToExternalOrder(row);
        MarketplaceSellerOrderStatus.Advance("woocommerce", "cancel", live);

        // Attempt 1: write inside a transaction and roll back.
        await using (var tx1 = await schemaDb.Database.BeginTransactionAsync())
        {
            await snapshots.UpdateStatusProjectionAsync(row.Id, live, clock.GetUtcNow().UtcDateTime, CancellationToken.None);
            await tx1.RollbackAsync();
        }

        // Attempt 2 on the SAME DbContext: retry and commit.
        await using (var tx2 = await schemaDb.Database.BeginTransactionAsync())
        {
            await snapshots.UpdateStatusProjectionAsync(row.Id, live, clock.GetUtcNow().UtcDateTime.AddSeconds(1), CancellationToken.None);
            await tx2.CommitAsync();
        }

        // Verify via a fresh DbContext that both status column and payloadJson persisted "cancelled".
        await using var freshDb = TestDbFactory.CreateSqliteContext(dsn);
        var freshSnapshots = new MarketplaceOrderSnapshotService(freshDb, clock);
        var freshRow = (await freshSnapshots.FindAsync(1, 10, "42", CancellationToken.None))!;
        Assert.Equal("cancelled", freshRow.status);
        Assert.Equal("cancelled", freshSnapshots.ToExternalOrder(freshRow).Status);
    }

    [Fact]
    public async Task AdmitOrderAction_NormalizesPaddedRefundKey_AndRejectsConflictingRefundAliases()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        await SeedStoreMemberAndConnectionAsync(db);

        var conn = await db.marketplaceConnections.SingleAsync(c => c.Id == 10);
        conn.adapterKey = "medusa";
        conn.credentialsJson = """{"apiBaseUrl":"https://shop.example/admin/delegat","apiKey":"secret"}""";
        await db.SaveChangesAsync();

        var clock = new ManualClock();
        var (adapter, _) = OrderAdapterProxy.Create();
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new Delegat.Marketplace.Medusa.MedusaPlugin(),
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 0 }, commerce);

        // Padded " refund " = "false" must normalize to "refund": false on the prepared Medusa cancel stage.
        var admitted = await exec.AdmitOrderActionAsync(
            conn, "ord_1", new OrderStatusUpdate
            {
                Action = "cancel",
                Extra = new Dictionary<string, string> { [" refund "] = " false ", [" note "] = "customer request" }
            }, 7, null, "medusa-cancel-padded-1", CancellationToken.None);

        var cmd = await db.integrationCommands.AsNoTracking().SingleAsync(c => c.commandId == admitted.CommandId);
        var stage = await db.integrationStages.AsNoTracking().SingleAsync(s => s.commandId == cmd.Id);
        using var payloadDoc = JsonDocument.Parse(stage.payloadJson!);
        Assert.False(payloadDoc.RootElement.GetProperty("refund").GetBoolean());
        Assert.Equal("customer request", payloadDoc.RootElement.GetProperty("note").GetString());

        // Conflicting refund aliases must be rejected before creating a command.
        var ex = await Assert.ThrowsAsync<ApiException>(() => exec.AdmitOrderActionAsync(
            conn, "ord_2", new OrderStatusUpdate
            {
                Action = "cancel",
                Extra = new Dictionary<string, string> { ["refund"] = "false", [" refund "] = "true" }
            }, 7, null, "medusa-cancel-conflict-1", CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("order_status_failed", ex.Code);
        Assert.Equal(1, await db.integrationCommands.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Cabinet_CreateShipment_RoutesThroughCoordinatorWithBoundedSyncWait_AndGetStatusExposesIntegration()
    {
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);
        await SeedStoreMemberAndConnectionAsync(schemaDb);

        var (adapter, proxy) = ShipmentAdapterProxy.Create();
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(schemaDb, new SingleAdapterRegistry(adapter), new MarketplaceOrderSnapshotService(schemaDb, clock));
        var coordinator = new IntegrationCoordinator(schemaDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => new FakeShipmentExecutionProvider(),
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 5000, PublicApiSyncPollIntervalMs = 20 }, commerce);

        var orderSync = new MarketplaceOrderSyncService(
            schemaDb, new SingleAdapterRegistry(adapter), new MarketplaceOrderSnapshotService(schemaDb, clock),
            clock, NullLogger<MarketplaceOrderSyncService>.Instance);
        var controller = new MarketplaceCommerceController(new StubCurrentUser(7), commerce, orderSync, exec)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        // 1. Without Prefer: respond-async, completes synchronously within bounded wait and NEVER calls legacy CreateShipmentAsync.
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "cab-ship-sync-1";
        var completer = Task.Run(async () =>
        {
            for (var i = 0; i < 120; i++)
            {
                await Task.Delay(25);
                await using var bgDb = TestDbFactory.CreateSqliteContext(dsn);
                var bgCmd = await bgDb.integrationCommands.FirstOrDefaultAsync(c => c.requestKey == "cab-ship-sync-1");
                if (bgCmd == null) continue;
                await bgDb.integrationStages
                    .Where(s => s.commandId == bgCmd.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.state, IntegrationStageState.Applied)
                        .SetProperty(x => x.evidenceJson, """{"shipmentId":"WB-GI-777"}"""));
                await bgDb.integrationCommands
                    .Where(c => c.Id == bgCmd.Id)
                    .ExecuteUpdateAsync(c => c
                        .SetProperty(x => x.state, IntegrationCommandState.Completed)
                        .SetProperty(x => x.finishedAt, (DateTime?)DateTime.UtcNow));
                return;
            }
        });

        var syncRes = await controller.CreateShipment(10, new CreateShipmentRequest { Name = "Cabinet Supply" }, CancellationToken.None);
        await completer;
        var ok = Assert.IsType<OkObjectResult>(syncRes);
        var dto = Assert.IsType<ShipmentMutationResult>(ok.Value);
        Assert.True(dto.Success);
        Assert.Equal("WB-GI-777", dto.Shipment?.ExternalId);
        Assert.Equal(0, proxy.LegacyCreateCalls);

        // 2. OperationsController.GetStatus exposes the integration summary (including shipmentId) for the created operation.
        var completedCmd = await schemaDb.integrationCommands.AsNoTracking().SingleAsync(c => c.requestKey == "cab-ship-sync-1");
        var opsController = new OperationsController(schemaDb, new StubCurrentUser(7), new NoopOpLogger(), storageService: null!, coordinator);
        var statusRes = await opsController.GetStatus(completedCmd.operationId!, CancellationToken.None);
        var statusOk = Assert.IsType<OkObjectResult>(statusRes);
        using var statusDoc = JsonDocument.Parse(JsonSerializer.Serialize(statusOk.Value));
        var integration = statusDoc.RootElement.GetProperty("integration");
        Assert.Equal("completed", integration.GetProperty("state").GetString());
        Assert.Equal("WB-GI-777", integration.GetProperty("result").GetProperty("shipmentId").GetString());

        // 3. With Prefer: respond-async, returns 202 Accepted immediately without waiting.
        controller.HttpContext.Request.Headers["Prefer"] = "respond-async";
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "cab-ship-async-1";
        var asyncRes = await controller.CreateShipment(10, new CreateShipmentRequest { Name = "Cabinet Async Supply" }, CancellationToken.None);
        Assert.IsType<AcceptedResult>(asyncRes);
        Assert.Equal(0, proxy.LegacyCreateCalls);

        // 4. Without Prefer header when the bounded wait expires (PublicApiSyncWaitMs = 0),
        // returns 202 with statusUrl and driving the real WildberriesPlugin through IntegrationExecutionWorker
        // produces AppliedEvidence that OperationsController.GetStatus exposes as integration.result.shipmentId.
        var wbConn = await schemaDb.marketplaceConnections.SingleAsync(c => c.Id == 10);
        wbConn.adapterKey = "wildberries";
        wbConn.credentialsJson = """{"apiKey":"wb-test-token"}""";
        await schemaDb.SaveChangesAsync();

        var wbPlugin = new Delegat.Marketplace.Wildberries.WildberriesPlugin();
        var fastTimeoutExec = new MarketplaceCommerceExecutionService(
            coordinator, new IntegrationPreparationService(), _ => wbPlugin,
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 0 }, commerce);
        var fastController = new MarketplaceCommerceController(new StubCurrentUser(7), commerce, orderSync, fastTimeoutExec)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        fastController.HttpContext.Request.Headers["Idempotency-Key"] = "cab-ship-202-poll-1";

        var slowRes = await fastController.CreateShipment(10, new CreateShipmentRequest { Name = "WB Slow Supply" }, CancellationToken.None);
        var slowAccepted = Assert.IsType<AcceptedResult>(slowRes);
        using var slowAcceptedDoc = JsonDocument.Parse(JsonSerializer.Serialize(slowAccepted.Value));
        var slowOpId = slowAcceptedDoc.RootElement.GetProperty("operationId").GetString()!;
        Assert.Equal($"/api/operations/{slowOpId}/status", slowAcceptedDoc.RootElement.GetProperty("statusUrl").GetString());

        // While still admitted/unexecuted, GetStatus reports admitted and no shipmentId yet.
        var pendingPoll = Assert.IsType<OkObjectResult>(await opsController.GetStatus(slowOpId, CancellationToken.None));
        using var pendingPollDoc = JsonDocument.Parse(JsonSerializer.Serialize(pendingPoll.Value));
        Assert.Equal("admitted", pendingPollDoc.RootElement.GetProperty("integration").GetProperty("state").GetString());

        // Drive real WildberriesPlugin.ExecuteStageAsync through IntegrationExecutionWorker.
        var wbServices = new ServiceCollection();
        wbServices.AddLogging();
        wbServices.AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn));
        wbServices.AddSingleton<TimeProvider>(clock);
        wbServices.AddSingleton<IOperationLogger, NoopOpLogger>();
        wbServices.AddSingleton<IIntegrationStageDispatcher>(new WildberriesTestSupplyDispatcher(wbPlugin, "WB-GI-909"));
        await using var wbProvider = wbServices.BuildServiceProvider();

        var wbWorker = new IntegrationExecutionWorker(
            wbProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);
        await wbWorker.ProcessDueStagesAsync(CancellationToken.None);

        await using var pollDb = TestDbFactory.CreateSqliteContext(dsn);
        var pollOpsController = new OperationsController(pollDb, new StubCurrentUser(7), new NoopOpLogger(), storageService: null!,
            new IntegrationCoordinator(pollDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance));
        var donePoll = Assert.IsType<OkObjectResult>(await pollOpsController.GetStatus(slowOpId, CancellationToken.None));
        using var donePollDoc = JsonDocument.Parse(JsonSerializer.Serialize(donePoll.Value));
        Assert.Equal("completed", donePollDoc.RootElement.GetProperty("integration").GetProperty("state").GetString());
        Assert.Equal("WB-GI-909", donePollDoc.RootElement.GetProperty("integration").GetProperty("result").GetProperty("shipmentId").GetString());
    }

    private sealed class StubWbSupplyTransport(string supplyId) : IMarketplaceTransport
    {
        public Task<IntegrationTransportResponse> SendAsync(IntegrationTransportRequest request, CancellationToken ct = default)
        {
            Assert.Equal("POST", request.Method);
            Assert.EndsWith("/api/v3/supplies", request.Url.AbsolutePath, StringComparison.Ordinal);
            return Task.FromResult(new IntegrationTransportResponse(
                201, new Dictionary<string, string>(), $$"""{"id":"{{supplyId}}"}"""));
        }
    }

    private sealed class WildberriesTestSupplyDispatcher(
        Delegat.Marketplace.Wildberries.WildberriesPlugin plugin,
        string supplyId) : IIntegrationStageDispatcher
    {
        public async Task<IntegrationDispatchResult> DispatchAsync(DispatchStageRequest request, CancellationToken ct)
        {
            var conn = new ConnectionContext
            {
                ConnectionId = 10,
                StoreId = 1,
                AdapterKey = "wildberries",
                Credentials = new Dictionary<string, string> { ["apiKey"] = "wb-test-token" },
                Transport = new StubWbSupplyTransport(supplyId)
            };
            var attempt = new IntegrationAttemptContext(
                request.TransportAttempt?.OperationId ?? request.CommandGuid,
                request.CommandGuid,
                request.ItemKey,
                request.StageKey,
                request.AttemptNumber,
                request.RequestKey);
            var stage = new IntegrationPreparedStage(
                request.StageKey,
                IntegrationStageKind.ShipmentAction,
                request.RequestKey,
                request.PayloadJson ?? "{}",
                [],
                IsEffectful: true);
            var outcome = await plugin.ExecuteStageAsync(new IntegrationStageRequest(conn, attempt, stage), ct);
            Assert.Equal(IntegrationStageOutcomeKind.Applied, outcome.Kind);
            return new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Applied,
                ReceiptJson: outcome.Receipt is null ? null : JsonSerializer.Serialize(outcome.Receipt),
                EvidenceJson: outcome.AppliedEvidence is null ? null : JsonSerializer.Serialize(outcome.AppliedEvidence));
        }

        public Task<IntegrationDispatchResult> ConfirmAsync(DispatchStageRequest request, CancellationToken ct) =>
            Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Unknown));
    }
}
