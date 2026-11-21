using System.Net;
using Delegat.Marketplace.Avito;
using Delegat.Marketplace.Medusa;
using Delegat.Marketplace.SDK;
using Delegat.Models.Integration;
using Delegat.Services;
using Delegat.Services.Integration;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class IntegrationExecutionP4HostTests
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

    private sealed class StaticBindingResolver(IntegrationExecutionBinding? binding)
        : IIntegrationExecutionBindingResolver
    {
        public Task<IntegrationExecutionBinding?> ResolveAsync(DispatchStageRequest request, CancellationToken ct) =>
            Task.FromResult(binding);
    }

    private sealed class FakeBrokenProvider(IntegrationPreparationResult result) : IMarketplaceExecutionProvider
    {
        public Task<IntegrationPreparationResult> PrepareAsync(IntegrationPreparationInput input, CancellationToken ct = default) =>
            Task.FromResult(result);
        public Task<IntegrationStageOutcome> ExecuteStageAsync(IntegrationStageRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IntegrationStageOutcome> ConfirmAsync(IntegrationConfirmationRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IntegrationStageOutcome> CancelAcceptedAsync(IntegrationConfirmationRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
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

    [Fact]
    public async Task Preparation_SingleAndBulkProductExport_PreserveVariantsMediaGroupsIdentically()
    {
        var prep = new IntegrationPreparationService();
        var medusa = new MedusaPlugin();
        var itemData = new Dictionary<string, object?>
        {
            ["sku"] = "SKU-1",
            ["name"] = "Shirt",
            ["price"] = "199.50",
            ["quantity"] = "7",
            ["group_id"] = "grp-summer",
            ["group_name"] = "Summer",
            ["_imageUrls"] = new List<string> { "https://cdn.example/1.jpg", "https://cdn.example/2.jpg" },
            ["_variants"] = new List<Dictionary<string, object>>
            {
                new() { ["id"] = "v-1", ["sku"] = "SKU-1-S", ["name"] = "Small", ["price"] = 199.50m, ["quantity"] = 3 },
                new() { ["id"] = "v-2", ["sku"] = "SKU-1-M", ["name"] = "Medium", ["price"] = 199.50m, ["quantity"] = 4 }
            }
        };

        var single = await prep.PrepareAsync(medusa, new IntegrationPreparationInput(
            IntegrationOperationKind.ProductExport, new ConnectionContext(),
            [new IntegrationItemIntent("p-1", itemData)], null, "cmd-1"), CancellationToken.None);

        var bulk = await prep.PrepareAsync(medusa, new IntegrationPreparationInput(
            IntegrationOperationKind.ProductExport, new ConnectionContext(),
            [new IntegrationItemIntent("p-1", itemData), new IntegrationItemIntent("p-2", new Dictionary<string, object?> { ["sku"] = "SKU-2", ["name"] = "Gadget" })],
            null, "cmd-2"), CancellationToken.None);

        Assert.False(single.HasBlockingIssues);
        Assert.False(bulk.HasBlockingIssues);

        var singleStage = Assert.Single(single.Stages, s => s.ItemKey == "p-1");
        var bulkStage = Assert.Single(bulk.Stages, s => s.ItemKey == "p-1");
        Assert.Equal(singleStage.PayloadJson, bulkStage.PayloadJson);
        Assert.Contains("SKU-1-S", singleStage.PayloadJson);
        Assert.Contains("SKU-1-M", singleStage.PayloadJson);
        Assert.Contains("https://cdn.example/1.jpg", singleStage.PayloadJson);
        Assert.Contains("grp-summer", singleStage.PayloadJson);
    }

    [Fact]
    public async Task Preparation_PreflightValidation_BlocksCyclesMissingDuplicateDepsAndBadPayloads()
    {
        var prep = new IntegrationPreparationService();
        var brokenProvider = new FakeBrokenProvider(new IntegrationPreparationResult(
        [
            new IntegrationPreparedItem("item-1",
            [
                new IntegrationPreparedStage("a", IntegrationStageKind.Card, "k1", "{}", ["b"]),
                new IntegrationPreparedStage("b", IntegrationStageKind.Price, "k2", "{}", ["a"])
            ]),
            new IntegrationPreparedItem("item-2",
            [
                new IntegrationPreparedStage("x", IntegrationStageKind.Card, "k3", "not-json", [])
            ]),
            new IntegrationPreparedItem("item-3",
            [
                new IntegrationPreparedStage("y", IntegrationStageKind.Price, "k4", "{}", ["nonexistent"])
            ]),
            new IntegrationPreparedItem("item-4",
            [
                new IntegrationPreparedStage("c", IntegrationStageKind.Card, "k5", "{}", []),
                new IntegrationPreparedStage("d", IntegrationStageKind.Price, "k6", "{}", ["c", "c"])
            ])
        ], []));

        var plan = await prep.PrepareAsync(brokenProvider, new IntegrationPreparationInput(
            IntegrationOperationKind.ProductExport, new ConnectionContext(),
            [
                new IntegrationItemIntent("item-1", new Dictionary<string, object?>()),
                new IntegrationItemIntent("item-2", new Dictionary<string, object?>()),
                new IntegrationItemIntent("item-3", new Dictionary<string, object?>()),
                new IntegrationItemIntent("item-4", new Dictionary<string, object?>())
            ], null, "cmd-bad"), CancellationToken.None);

        Assert.True(plan.HasBlockingIssues);
        Assert.Empty(plan.Stages);
        Assert.Contains(plan.Issues, i => i.Code == "cyclic_stage_dependency" && i.ItemKey == "item-1");
        Assert.Contains(plan.Issues, i => i.Code == "invalid_stage_payload" && i.ItemKey == "item-2");
        Assert.Contains(plan.Issues, i => i.Code == "missing_stage_dependency" && i.ItemKey == "item-3");
        Assert.Contains(plan.Issues, i => i.Code == "duplicate_stage_dependency" && i.ItemKey == "item-4");
    }

    [Fact]
    public async Task Avito_PriceExport_EndToEnd_HandlesAdmittedAuthSemanticRejectionThrottleRetryAndLostResponseReconciliation()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var avito = new AvitoPlugin();
        var prep = new IntegrationPreparationService();
        var plan = await prep.PrepareAsync(avito, new IntegrationPreparationInput(
            IntegrationOperationKind.PriceExport, new ConnectionContext(),
            [new IntegrationItemIntent("item-1", new Dictionary<string, object?> { ["price"] = "2500" }, ExpectedRemoteId: "123456")],
            null, "cmd-avito"), CancellationToken.None);
        Assert.False(plan.HasBlockingIssues);

        var seenPaths = new List<string>();
        var priceCalls = 0;
        var binding = new IntegrationExecutionBinding(
            avito,
            new ConnectionContext
            {
                AdapterKey = "avito",
                Credentials = new Dictionary<string, string> { ["clientId"] = "cid", ["clientSecret"] = "csec" }
            },
            new IntegrationHostRouteBinding(
                IntegrationProviderKind.Avito, IntegrationProviderEnvironment.Production,
                new Uri("https://api.avito.ru"), VerifiedSellerId: "avito-user-1"));

        var services = new ServiceCollection()
            .AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IOperationLogger, NoopOpLogger>()
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>()
            .AddSingleton<IIntegrationExecutionBindingResolver>(new StaticBindingResolver(binding))
            .AddSingleton<IIntegrationStageDispatcher, IntegrationProviderStageDispatcher>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((req, _) =>
            {
                seenPaths.Add($"{req.Method} {req.RequestUri!.AbsolutePath}");
                if (req.RequestUri.AbsolutePath == "/token")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"tok-1"}""") });
                if (req.RequestUri.AbsolutePath == "/core/v1/items/123456/update_price")
                {
                    priceCalls++;
                    if (priceCalls == 1)
                        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("slow") });
                    // Second attempt applies remotely but drops connection (IOException -> status 0).
                    throw new IOException("connection dropped after provider applied price");
                }
                // Confirmation read-back verifies the price 2500 without replaying the write.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"resources":[{"id":123456,"price":2500}]}""")
                });
            }));
        await using var provider = services.BuildServiceProvider();

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(new IntegrationAdmitRequest(
                "PriceExport", 1, 10, null, 7, "avito-price-1", "{}", "Avito price", plan.Stages),
                v1Enabled: true, CancellationToken.None);
        }

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);

        // Poll 1: 429 -> Retryable Rejected -> stays Ready for retry.
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(1, priceCalls);

        // Poll 2: retries dispatch -> connection drops (status 0) -> schedules ConfirmAsync (Pending -> Deferred).
        clock.Now = clock.Now.AddMinutes(2);
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(2, priceCalls);

        // Poll 3: ConfirmAsync reads GET /core/v1/items and confirms Applied WITHOUT a 3rd update_price!
        clock.Now = clock.Now.AddMinutes(2);
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(2, priceCalls);
        Assert.Contains("GET /core/v1/items", seenPaths);

        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Equal(IntegrationCommandState.Completed, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }

    [Fact]
    public async Task Avito_Semantic200SuccessFalse_IsRejectedNotApplied()
    {
        var avito = new AvitoPlugin();
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var prep = new IntegrationPreparationService();
        var plan = await prep.PrepareAsync(avito, new IntegrationPreparationInput(
            IntegrationOperationKind.PriceExport, new ConnectionContext(),
            [new IntegrationItemIntent("item-1", new Dictionary<string, object?> { ["price"] = "2500" }, ExpectedRemoteId: "123456")],
            null, "cmd-avito-rej"), CancellationToken.None);

        var binding = new IntegrationExecutionBinding(
            avito,
            new ConnectionContext
            {
                AdapterKey = "avito",
                Credentials = new Dictionary<string, string> { ["accessToken"] = "tok-1" }
            },
            new IntegrationHostRouteBinding(
                IntegrationProviderKind.Avito, IntegrationProviderEnvironment.Production,
                new Uri("https://api.avito.ru"), VerifiedSellerId: "avito-user-2"));

        var services = new ServiceCollection()
            .AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IOperationLogger, NoopOpLogger>()
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>()
            .AddSingleton<IIntegrationExecutionBindingResolver>(new StaticBindingResolver(binding))
            .AddSingleton<IIntegrationStageDispatcher, IntegrationProviderStageDispatcher>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"result":{"success":false}}""")
                })));
        await using var provider = services.BuildServiceProvider();

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(new IntegrationAdmitRequest(
                "PriceExport", 1, 10, null, 7, "avito-rej-1", "{}", "Avito semantic reject", plan.Stages),
                v1Enabled: true, CancellationToken.None);
        }

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Equal(IntegrationStageState.RejectedBeforeEffect, (await verify.integrationStages.AsNoTracking().SingleAsync()).state);
        Assert.Equal(IntegrationCommandState.Failed, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }

    [Fact]
    public async Task Medusa_BridgeRootCredential_ResolvesUpsertEndpointAndRejectsSemanticFalse()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var medusa = new MedusaPlugin();
        var prep = new IntegrationPreparationService();
        var plan = await prep.PrepareAsync(medusa, new IntegrationPreparationInput(
            IntegrationOperationKind.ProductExport, new ConnectionContext(),
            [new IntegrationItemIntent("p-1", new Dictionary<string, object?> { ["sku"] = "MED-1", ["name"] = "Shirt", ["price"] = "49.90" })],
            null, "cmd-med"), CancellationToken.None);
        Assert.False(plan.HasBlockingIssues);

        var seenPaths = new List<string>();
        var callCount = 0;
        var origin = new Uri("https://medusa.shop.test");
        var binding = new IntegrationExecutionBinding(
            medusa,
            new ConnectionContext
            {
                AdapterKey = "medusa",
                Credentials = new Dictionary<string, string>
                {
                    // Documented bridge root includes /admin/delegat — must NOT duplicate it.
                    ["apiBaseUrl"] = "https://medusa.shop.test/admin/delegat",
                    ["apiKey"] = "bridge-secret"
                }
            },
            new IntegrationHostRouteBinding(
                IntegrationProviderKind.Medusa, IntegrationProviderEnvironment.Production,
                origin, VerifiedSellerId: "medusa-shop-1"));

        var services = new ServiceCollection()
            .AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IOperationLogger, NoopOpLogger>()
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>()
            .AddSingleton<IIntegrationExecutionBindingResolver>(new StaticBindingResolver(binding))
            .AddSingleton<IIntegrationStageDispatcher, IntegrationProviderStageDispatcher>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((req, _) =>
            {
                seenPaths.Add($"{req.Method} {req.RequestUri!.AbsolutePath}");
                callCount++;
                if (callCount == 1)
                    return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("busy") });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true,"product_id":"prod_123"}""")
                });
            }));
        await using var provider = services.BuildServiceProvider();

        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
            await coordinator.AdmitAsync(new IntegrationAdmitRequest(
                "ProductExport", 1, 10, null, 8, "med-1", "{}", "Medusa export", plan.Stages),
                v1Enabled: true, CancellationToken.None);
        }

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance, clock);

        // Poll 1: 429 -> Retryable Rejected (stays Ready).
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        // Poll 2: retries and succeeds at /admin/delegat/products/upsert!
        clock.Now = clock.Now.AddMinutes(2);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal(["POST /admin/delegat/products/upsert", "POST /admin/delegat/products/upsert"], seenPaths);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Equal(IntegrationCommandState.Completed, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }
}
