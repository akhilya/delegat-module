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
    public async Task Preparation_SingleAndBulkProductExport_ProduceIdenticalStagePlan()
    {
        var prep = new IntegrationPreparationService();
        var medusa = new MedusaPlugin();
        var itemData = new Dictionary<string, object?>
        {
            ["sku"] = "SKU-1",
            ["name"] = "Widget",
            ["price"] = "199.50",
            ["quantity"] = "7"
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

        var singleStages = single.Stages.Where(s => s.ItemKey == "p-1").ToList();
        var bulkStages = bulk.Stages.Where(s => s.ItemKey == "p-1").ToList();
        Assert.Equal(3, singleStages.Count);
        Assert.Equal(singleStages.Count, bulkStages.Count);
        for (var i = 0; i < singleStages.Count; i++)
        {
            Assert.Equal(singleStages[i].StageKey, bulkStages[i].StageKey);
            Assert.Equal(singleStages[i].Kind, bulkStages[i].Kind);
            Assert.Equal(singleStages[i].PayloadJson, bulkStages[i].PayloadJson);
            Assert.Equal(singleStages[i].DependsOn, bulkStages[i].DependsOn);
        }
    }

    [Fact]
    public async Task Preparation_PreflightValidation_BlocksCyclesMissingDepsAndBadPayloads()
    {
        var prep = new IntegrationPreparationService();
        var cyclicProvider = new FakeBrokenProvider(new IntegrationPreparationResult(
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
            ])
        ], []));

        var plan = await prep.PrepareAsync(cyclicProvider, new IntegrationPreparationInput(
            IntegrationOperationKind.ProductExport, new ConnectionContext(),
            [
                new IntegrationItemIntent("item-1", new Dictionary<string, object?>()),
                new IntegrationItemIntent("item-2", new Dictionary<string, object?>()),
                new IntegrationItemIntent("item-3", new Dictionary<string, object?>())
            ], null, "cmd-bad"), CancellationToken.None);

        Assert.True(plan.HasBlockingIssues);
        Assert.Empty(plan.Stages);
        Assert.Contains(plan.Issues, i => i.Code == "cyclic_stage_dependency" && i.ItemKey == "item-1");
        Assert.Contains(plan.Issues, i => i.Code == "invalid_stage_payload" && i.ItemKey == "item-2");
        Assert.Contains(plan.Issues, i => i.Code == "missing_stage_dependency" && i.ItemKey == "item-3");
    }

    [Fact]
    public async Task Avito_PriceExport_EndToEndThroughWorkerTransportAndAdmittedAuth()
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
        var binding = new IntegrationExecutionBinding(
            avito,
            new ConnectionContext
            {
                AdapterKey = "avito",
                Credentials = new Dictionary<string, string>
                {
                    ["clientId"] = "cid",
                    ["clientSecret"] = "csec"
                }
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
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"result":{"success":true}}""") });
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
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        // Both OAuth /token AND /core/v1/items/{id}/update_price flowed through admitted V1 transport!
        Assert.Equal(["POST /token", "POST /core/v1/items/123456/update_price"], seenPaths);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Equal(IntegrationCommandState.Completed, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }

    [Fact]
    public async Task Avito_UnsupportedOperationAndInvalidInput_BlockedBeforeMutation()
    {
        var avito = new AvitoPlugin();
        var prep = new IntegrationPreparationService();

        var unsupported = await prep.PrepareAsync(avito, new IntegrationPreparationInput(
            IntegrationOperationKind.ProductExport, new ConnectionContext(),
            [new IntegrationItemIntent("p-1", new Dictionary<string, object?> { ["name"] = "Test" })],
            null, "cmd-u"), CancellationToken.None);
        Assert.True(unsupported.HasBlockingIssues);
        Assert.Empty(unsupported.Stages);
        Assert.Contains(unsupported.Issues, i => i.Code == "unsupported_operation");

        var badPrice = await prep.PrepareAsync(avito, new IntegrationPreparationInput(
            IntegrationOperationKind.PriceExport, new ConnectionContext(),
            [new IntegrationItemIntent("p-2", new Dictionary<string, object?> { ["price"] = "-5" }, ExpectedRemoteId: "123")],
            null, "cmd-b"), CancellationToken.None);
        Assert.True(badPrice.HasBlockingIssues);
        Assert.Empty(badPrice.Stages);
        Assert.Contains(badPrice.Issues, i => i.Code == "invalid_price");
    }

    [Fact]
    public async Task Medusa_BridgeAccepted202_ConfirmsViaStatusReceiptAndCompletesDependents()
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
        Assert.Equal(2, plan.Stages.Count);

        var seenPaths = new List<string>();
        var origin = new Uri("https://medusa.shop.test");
        var binding = new IntegrationExecutionBinding(
            medusa,
            new ConnectionContext
            {
                AdapterKey = "medusa",
                Credentials = new Dictionary<string, string>
                {
                    ["apiBaseUrl"] = origin.ToString(),
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
                if (req.RequestUri.AbsolutePath == "/admin/delegat/products")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""{"taskId":"t-med-7"}""") });
                if (req.RequestUri.AbsolutePath == "/admin/delegat/status/t-med-7")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"status":"applied"}""") });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"price-1"}""") });
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

        // Poll 1: card -> 202 Accepted (receipt t-med-7); price stage waits on card dependency!
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        Assert.Equal(["POST /admin/delegat/products"], seenPaths);

        // Poll 2 (after confirm delay): card confirms Applied via GET /admin/delegat/status/t-med-7, then price dispatches!
        clock.Now = clock.Now.AddMinutes(1);
        await worker.ProcessDueStagesAsync(CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(1);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.Equal([
            "POST /admin/delegat/products",
            "GET /admin/delegat/status/t-med-7",
            "POST /admin/delegat/prices"
        ], seenPaths);

        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Equal(IntegrationCommandState.Completed, (await verify.integrationCommands.AsNoTracking().SingleAsync()).state);
    }
}
