using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Delegat.Controllers;
using Delegat.Controllers.PublicApi;
using Delegat.Enums;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Marketplace.Avito;
using Delegat.Marketplace.Medusa;
using Delegat.Marketplace.OpenCart;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.WooCommerce;
using Delegat.Marketplace.YandexMarket;
using Delegat.Models;
using Delegat.Models.Integration;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Services.Integration;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Tools;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HostCaps = Delegat.Services.Marketplace.MarketplaceCapabilities;

namespace Delegat.Tests;

/// <summary>
/// End-to-end verification of V1 durable routing for catalog product exports,
/// product group exports, price/stock exports (cabinet, Public API, scheduled
/// actions, platform tools), async worker link projection after 202 Accepted,
/// operation-kind adapter gating, and A01 single-vs-bulk/1-item-tail conformance
/// across all 7 marketplace adapters.
/// </summary>
public sealed class IntegrationExecutionP8CatalogRoutingTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubCurrentUser(int userId, int storeId = 1) : ICurrentUserService
    {
        public int? GetUserId() => userId;
        public Task<User?> GetUserAsync(CancellationToken ct = default) => Task.FromResult<User?>(null);
        public int? GetSelectedStoreId() => storeId;
        public Task<int?> GetSelectedStoreIdAsync(CancellationToken ct = default) => Task.FromResult<int?>(storeId);
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
        public IMarketplaceAdapter? GetByKey(string key) =>
            string.Equals(key, adapter.Key, StringComparison.OrdinalIgnoreCase) ? adapter : null;
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public bool Exists(string key) => GetByKey(key) != null;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private sealed class StubTransport(
        Func<IntegrationTransportRequest, CancellationToken, Task<IntegrationTransportResponse>> send) : IMarketplaceTransport
    {
        public Task<IntegrationTransportResponse> SendAsync(IntegrationTransportRequest request, CancellationToken ct = default) =>
            send(request, ct);
    }

    private sealed class DirectProviderStageDispatcher(
        IMarketplaceExecutionProvider provider,
        string adapterKey,
        Dictionary<string, string> creds,
        Func<IntegrationTransportRequest, CancellationToken, Task<IntegrationTransportResponse>> send) : IIntegrationStageDispatcher
    {
        public async Task<IntegrationDispatchResult> DispatchAsync(DispatchStageRequest request, CancellationToken ct)
        {
            var conn = new ConnectionContext
            {
                ConnectionId = 10,
                StoreId = 1,
                AdapterKey = adapterKey,
                Credentials = creds,
                Transport = new StubTransport(send)
            };
            var attempt = new IntegrationAttemptContext(
                request.TransportAttempt?.OperationId ?? request.CommandGuid,
                request.CommandGuid,
                request.ItemKey,
                request.StageKey,
                request.AttemptNumber,
                request.RequestKey);
            var stageKind = Enum.TryParse<IntegrationStageKind>(request.StageKind, ignoreCase: true, out var parsed)
                ? parsed
                : IntegrationStageKind.Card;
            var stage = new IntegrationPreparedStage(
                request.StageKey,
                stageKind,
                request.RequestKey,
                request.PayloadJson ?? "{}",
                [],
                IsEffectful: true);

            var outcome = await provider.ExecuteStageAsync(new IntegrationStageRequest(conn, attempt, stage), ct);
            var mappedOutcome = outcome.Kind switch
            {
                IntegrationStageOutcomeKind.Applied => IntegrationDispatchOutcome.Applied,
                IntegrationStageOutcomeKind.Accepted => IntegrationDispatchOutcome.Accepted,
                IntegrationStageOutcomeKind.Pending => IntegrationDispatchOutcome.Pending,
                IntegrationStageOutcomeKind.Rejected => IntegrationDispatchOutcome.Rejected,
                _ => IntegrationDispatchOutcome.Unknown
            };
            var issues = outcome.Issues;
            return new IntegrationDispatchResult(
                mappedOutcome,
                Retryable: issues is { Count: > 0 } && issues.Any(i => i.Retryable),
                ReceiptJson: outcome.Receipt is null ? null : JsonSerializer.Serialize(outcome.Receipt),
                EvidenceJson: outcome.AppliedEvidence is null ? null : JsonSerializer.Serialize(outcome.AppliedEvidence),
                IssuesJson: issues is not { Count: > 0 } ? null : JsonSerializer.Serialize(issues));
        }

        public Task<IntegrationDispatchResult> ConfirmAsync(DispatchStageRequest request, CancellationToken ct) =>
            Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Unknown));
    }

    private sealed class FakeAdapter(string key, HostCaps caps)
        : BaseMarketplaceAdapter(new HttpClient(), NullLogger.Instance)
    {
        public int LegacyExportProductCalls { get; private set; }
        public int LegacyExportBulkCalls { get; private set; }
        public int LegacyExportPricesCalls { get; private set; }
        public int LegacyExportStocksCalls { get; private set; }
        public int LegacyCreateGroupCalls { get; private set; }
        public int LegacyAddToGroupCalls { get; private set; }

        public override string Key => key;
        public override string DisplayName => key;
        public override HostCaps Capabilities => caps;

        public override Delegat.Services.Marketplace.CredentialsSchema GetCredentialsSchema() => new();

        public override Task<ConnectionTestResult> TestConnectionAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true, "ok"));

        public override Task<ExportResult> ExportProductAsync(
            MarketplaceConnection connection, Dictionary<string, object> productData, string? existingExternalId = null, CancellationToken ct = default)
        {
            LegacyExportProductCalls++;
            return Task.FromResult(new ExportResult(true, existingExternalId ?? "legacy-prod-1"));
        }

        public override Task<BulkExportResult> ExportProductsBulkAsync(
            MarketplaceConnection connection, List<BulkExportItem> items, CancellationToken ct = default)
        {
            LegacyExportBulkCalls++;
            return Task.FromResult(new BulkExportResult(
                items.Count, items.Count, 0,
                items.Select(i => new ExportItemResult(i.LocalProductId, true, $"legacy-{i.LocalProductId}")).ToList()));
        }

        public override Task<ImportResult> ExportPricesAsync(
            MarketplaceConnection connection, List<PriceInfo> prices, CancellationToken ct = default)
        {
            LegacyExportPricesCalls++;
            return Task.FromResult(new ImportResult(prices.Count, prices.Count, 0));
        }

        public override Task<ImportResult> ExportStocksAsync(
            MarketplaceConnection connection, List<StockInfo> stocks, CancellationToken ct = default)
        {
            LegacyExportStocksCalls++;
            return Task.FromResult(new ImportResult(stocks.Count, stocks.Count, 0));
        }

        public override Task<CreateGroupResult> CreateGroupAsync(
            MarketplaceConnection connection, List<string> productExternalIds, string? groupName = null, CancellationToken ct = default)
        {
            LegacyCreateGroupCalls++;
            return Task.FromResult(new CreateGroupResult(true, "legacy-grp-1"));
        }

        public override Task<GroupMembershipResult> AddProductsToGroupAsync(
            MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
        {
            LegacyAddToGroupCalls++;
            return Task.FromResult(new GroupMembershipResult(true));
        }
    }

    private static IntegrationExecutionWorker BuildWorker(
        string dsn,
        ManualClock clock,
        IMarketplaceExecutionProvider provider,
        string adapterKey,
        Dictionary<string, string> creds,
        Func<IntegrationTransportRequest, CancellationToken, Task<IntegrationTransportResponse>> send)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn));
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOperationLogger, NoopOpLogger>();
        services.AddSingleton<IIntegrationStageDispatcher>(
            new DirectProviderStageDispatcher(provider, adapterKey, creds, send));
        var sp = services.BuildServiceProvider();
        return new IntegrationExecutionWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IntegrationExecutionWorker>.Instance,
            clock);
    }

    private static async Task SeedCatalogAsync(DataContext db, string adapterKey = "medusa")
    {
        var user = new User { Id = 7, UserName = "u@example.com", Email = "u@example.com", isActive = true };
        var store = new Store { Id = 1, name = "Store 1", baseCurrency = "RUB", members = [user] };
        var category = new Category { Id = 100, storeId = 1, name = "Shoes" };
        var conn = new MarketplaceConnection
        {
            Id = 10,
            storeId = 1,
            name = "Medusa Store",
            adapterKey = adapterKey,
            credentialsJson = """{"apiBaseUrl":"https://medusa.example.com","apiKey":"med-key-1"}""",
            isActive = true
        };
        var p1 = new Product { Id = 101, storeId = 1, categoryId = 100, name = "Sneaker One", description = "Sneaker One desc", sku = "SKU-101", price = 4500m, storageQuantity = 12 };
        var p2 = new Product { Id = 102, storeId = 1, categoryId = 100, name = "Sneaker Two", description = "Sneaker Two desc", sku = "SKU-102", price = 5200m, storageQuantity = 8 };
        var mapping = new CategoryMapping
        {
            Id = 200,
            connectionId = 10,
            localCategoryId = 100,
            externalCategoryId = "pcat_shoes",
            externalCategoryName = "Shoes",
            graphJson = GraphGenerator.BuildGraphJson(
            [
                new FieldSpec { externalFieldKey = "name", externalFieldName = "Name", externalFieldType = "string", sourceType = FieldMappingSourceType.ProductField, sourceField = "name" },
                new FieldSpec { externalFieldKey = "sku", externalFieldName = "SKU", externalFieldType = "string", sourceType = FieldMappingSourceType.ProductField, sourceField = "sku" },
                new FieldSpec { externalFieldKey = "price", externalFieldName = "Price", externalFieldType = "number", sourceType = FieldMappingSourceType.ProductField, sourceField = "price" }
            ])
        };
        db.Users.Add(user);
        db.stores.Add(store);
        db.categories.Add(category);
        db.marketplaceConnections.Add(conn);
        db.products.AddRange(p1, p2);
        db.categoryMappings.Add(mapping);
        await db.SaveChangesAsync();
    }

    [Fact]
    public void CanExecute_RespectsAdapterSupportedOperationKinds()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        using var _ = keeper;
        using var __ = db;

        var providers = new Dictionary<string, IMarketplaceExecutionProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["avito"] = new AvitoPlugin(),
            ["medusa"] = new MedusaPlugin(),
            ["opencart"] = new OpenCartPlugin(),
            ["ozon"] = new OzonPlugin(),
            ["wildberries"] = new WildberriesPlugin(),
            ["woocommerce"] = new WooCommercePlugin(),
            ["yandex-market"] = new YandexMarketPlugin()
        };

        var clock = new ManualClock();
        var adapter = new FakeAdapter("medusa", HostCaps.ExportProduct | HostCaps.ExportPrices | HostCaps.ExportStocks);
        var commerce = new MarketplaceCommerceService(db, new SingleAdapterRegistry(adapter), new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator,
            new IntegrationPreparationService(),
            key => providers.GetValueOrDefault(key),
            new IntegrationExecutionOptions { Enabled = true },
            commerce);

        Assert.True(exec.CanExecute("avito", IntegrationOperationKind.PriceExport));
        Assert.False(exec.CanExecute("avito", IntegrationOperationKind.StockExport));
        Assert.False(exec.CanExecute("avito", IntegrationOperationKind.ProductExport));
        Assert.False(exec.CanExecute("avito", IntegrationOperationKind.GroupUpdate));

        Assert.True(exec.CanExecute("medusa", IntegrationOperationKind.GroupUpdate));
        Assert.True(exec.CanExecute("medusa", IntegrationOperationKind.ProductExport));
        Assert.False(exec.CanExecute("medusa", IntegrationOperationKind.PromotionAction));

        Assert.False(exec.CanExecute("woocommerce", IntegrationOperationKind.GroupUpdate));
        Assert.True(exec.CanExecute("woocommerce", IntegrationOperationKind.PromotionAction));
        Assert.True(exec.CanExecute("wildberries", IntegrationOperationKind.ShipmentAction));
        Assert.True(exec.CanExecute("ozon", IntegrationOperationKind.ShipmentAction));
    }

    [Fact]
    public async Task GroupSync_V1Medusa_Returns202Pending_AndWorkerProjectsConfirmedLinkAndOperationResult()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        await SeedCatalogAsync(db, "medusa");

        db.productMarketplaceLinks.AddRange(
            new ProductMarketplaceLink { Id = 301, productId = 101, connectionId = 10, externalProductId = "prod_101", status = ProductMarketplaceStatus.Synced },
            new ProductMarketplaceLink { Id = 302, productId = 102, connectionId = 10, externalProductId = "prod_102", status = ProductMarketplaceStatus.Synced });
        var group = new ProductGroup
        {
            Id = 50,
            storeId = 1,
            name = "Summer Collection",
            members =
            [
                new ProductGroupMember { productId = 101, sortOrder = 0 },
                new ProductGroupMember { productId = 102, sortOrder = 1 }
            ]
        };
        db.productGroups.Add(group);
        await db.SaveChangesAsync();

        var adapter = new FakeAdapter("medusa", HostCaps.EndpointBasedGrouping | HostCaps.ExportProduct);
        var registry = new SingleAdapterRegistry(adapter);
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, registry, new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator,
            new IntegrationPreparationService(),
            key => key == "medusa" ? new MedusaPlugin() : null,
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 0 },
            commerce);

        var groupSync = new GroupSyncService(db, registry, NullLogger<GroupSyncService>.Instance, exec);
        var sp = new ServiceCollection().BuildServiceProvider();
        var controller = new ProductGroupController(
            new StubCurrentUser(7),
            new UnitOfWork(db),
            db,
            groupSync,
            sp,
            NullLogger<ProductGroupController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Request.Headers["Idempotency-Key"] = "grp-50-export";

        var actionResult = await controller.ExportGroup(50, 10);
        var accepted = Assert.IsType<AcceptedResult>(actionResult);
        Assert.Equal(202, accepted.StatusCode);
        Assert.Equal(0, adapter.LegacyCreateGroupCalls);
        Assert.Equal(0, adapter.LegacyAddToGroupCalls);

        // No premature Synced link while durable command is pending.
        Assert.Null(await db.productGroupMarketplaceLinks.AsNoTracking().FirstOrDefaultAsync(l => l.productGroupId == 50));

        // Drive the real MedusaPlugin through IntegrationExecutionWorker.
        string? capturedBody = null;
        var worker = BuildWorker(
            dsn, clock, new MedusaPlugin(), "medusa",
            new Dictionary<string, string> { ["apiBaseUrl"] = "https://medusa.example.com", ["apiKey"] = "med-key-1" },
            (req, _) =>
            {
                capturedBody = req.Body;
                return Task.FromResult(new IntegrationTransportResponse(
                    200, new Dictionary<string, string>(), """{"success":true,"collection_id":"pcol_50"}"""));
            });

        await worker.ProcessDueStagesAsync(CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"product_ids\":[\"prod_101\",\"prod_102\"]", capturedBody);
        Assert.Contains("\"delegat_group_id\":\"50\"", capturedBody);

        // Worker projected the confirmed ProductGroupMarketplaceLink atomically on completion.
        await using var verifyDb = TestDbFactory.CreateSqliteContext(dsn);
        var link = await verifyDb.productGroupMarketplaceLinks.AsNoTracking()
            .SingleAsync(l => l.productGroupId == 50 && l.connectionId == 10);
        Assert.Equal("pcol_50", link.externalGroupId);
        Assert.Equal(ProductMarketplaceStatus.Synced, link.status);

        // OperationsController.GetStatus exposes externalGroupId under integration.result.
        var op = await verifyDb.operations.AsNoTracking().SingleAsync();
        var opsController = new OperationsController(
            verifyDb, new StubCurrentUser(7), new NoopOpLogger(), storageService: null!,
            new IntegrationCoordinator(verifyDb, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance));
        var statusRes = Assert.IsType<OkObjectResult>(await opsController.GetStatus(op.operationId, CancellationToken.None));
        var statusJson = JsonSerializer.Serialize(statusRes.Value);
        using var doc = JsonDocument.Parse(statusJson);
        Assert.Equal("pcol_50", doc.RootElement.GetProperty("integration").GetProperty("result").GetProperty("externalGroupId").GetString());
    }

    [Fact]
    public async Task PriceAndStockSync_V1RoutesAcrossCabinetPublicApiScheduledAndTools_AndProjectsConfirmedCachesOnly()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        await SeedCatalogAsync(db, "medusa");

        db.productMarketplaceLinks.AddRange(
            new ProductMarketplaceLink { Id = 401, productId = 101, connectionId = 10, externalProductId = "prod_101", externalPrice = 1000m, externalStock = 1, status = ProductMarketplaceStatus.Synced },
            new ProductMarketplaceLink { Id = 402, productId = 102, connectionId = 10, externalProductId = "prod_102", externalPrice = 1000m, externalStock = 1, status = ProductMarketplaceStatus.Synced });
        await db.SaveChangesAsync();

        var adapter = new FakeAdapter("medusa", HostCaps.ExportPrices | HostCaps.ExportStocks);
        var registry = new SingleAdapterRegistry(adapter);
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, registry, new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator,
            new IntegrationPreparationService(),
            key => key == "medusa" ? new MedusaPlugin() : null,
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 0 },
            commerce);

        var sp = new ServiceCollection().BuildServiceProvider();
        var mappingSvc = new MappingService(db, sp);
        var currencySvc = new CurrencyService(db);
        var priceSync = new PriceSyncService(db, registry, mappingSvc, new NoopOpLogger(), currencySvc, exec);

        // 1. Price export via PriceSyncService returns pending without prematurely mutating externalPrice.
        var priceRes = await priceSync.ExportPricesAsync(10, null, CancellationToken.None, authorUserId: 7, requestKey: "p-exp-1");
        Assert.True(priceRes.IsPending);
        Assert.Equal(0, adapter.LegacyExportPricesCalls);
        Assert.Equal(1000m, (await db.productMarketplaceLinks.AsNoTracking().SingleAsync(l => l.Id == 401)).externalPrice);

        // Drive worker: item 401 succeeds, item 402 returns 400 non-retryable rejection.
        var worker = BuildWorker(
            dsn, clock, new MedusaPlugin(), "medusa",
            new Dictionary<string, string> { ["apiBaseUrl"] = "https://medusa.example.com", ["apiKey"] = "med-key-1" },
            (req, _) =>
            {
                if (req.Body != null && req.Body.Contains("prod_102"))
                {
                    return Task.FromResult(new IntegrationTransportResponse(
                        400, new Dictionary<string, string>(), """{"error":"invalid_product"}"""));
                }
                return Task.FromResult(new IntegrationTransportResponse(
                    200, new Dictionary<string, string>(), """{"success":true}"""));
            });

        await worker.ProcessDueStagesAsync(CancellationToken.None);

        // Only confirmed Applied link 401 updated externalPrice; rejected link 402 kept 1000m!
        await using (var verifyDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var link401 = await verifyDb.productMarketplaceLinks.AsNoTracking().SingleAsync(l => l.Id == 401);
            var link402 = await verifyDb.productMarketplaceLinks.AsNoTracking().SingleAsync(l => l.Id == 402);
            Assert.Equal(4500m, link401.externalPrice);
            Assert.Equal(1000m, link402.externalPrice);
        }

        // 2. PublicSyncController.ExportStocks returns 202 via coordinator (no Task.Run, 0 legacy calls).
        var publicHttp = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("apiKeyId", "55")], "ApiKey"))
        };
        publicHttp.Request.Headers["Idempotency-Key"] = "pub-stock-1";
        var publicController = new PublicSyncController(
            new PublicApiStoreContext(new StubCurrentUser(7), db),
            db,
            new NoopOpLogger(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PublicSyncController>.Instance,
            quotaService: null!,
            priceSync)
        {
            ControllerContext = new ControllerContext { HttpContext = publicHttp }
        };
        var stockHttpRes = await publicController.ExportStocks(1, 10, null, CancellationToken.None);
        var stockAccepted = Assert.IsType<AcceptedResult>(stockHttpRes);
        Assert.Equal(202, stockAccepted.StatusCode);
        Assert.Equal(0, adapter.LegacyExportStocksCalls);

        // Drive worker to confirm both stock updates and project externalStock.
        var stockWorker = BuildWorker(
            dsn, clock, new MedusaPlugin(), "medusa",
            new Dictionary<string, string> { ["apiBaseUrl"] = "https://medusa.example.com", ["apiKey"] = "med-key-1" },
            (_, _) => Task.FromResult(new IntegrationTransportResponse(
                200, new Dictionary<string, string>(), """{"success":true}""")));
        await stockWorker.ProcessDueStagesAsync(CancellationToken.None);

        await using (var verifyDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            Assert.Equal(12, (await verifyDb.productMarketplaceLinks.AsNoTracking().SingleAsync(l => l.Id == 401)).externalStock);
            Assert.Equal(8, (await verifyDb.productMarketplaceLinks.AsNoTracking().SingleAsync(l => l.Id == 402)).externalStock);
        }

        // 3. MarketplaceActionExecutor (ScheduledTasks) and PlatformToolJobService route through V1 with 0 legacy calls.
        var productSync = new ProductSyncService(
            db, registry, mappingSvc, storageService: null!,
            NullLogger<ProductSyncService>.Instance, new NoopOpLogger(), quotaService: null!,
            mediaExportGrants: null, commerceExecution: exec);
        var actionExecutor = new MarketplaceActionExecutor(db, registry, priceSync, productSync);
        var conn = await db.marketplaceConnections.SingleAsync(c => c.Id == 10);
        var scheduledSummary = await actionExecutor.ExecuteAsync("export-prices", conn, 1, 7, null, "sched-op-1", CancellationToken.None);
        Assert.Contains("durable", scheduledSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, adapter.LegacyExportPricesCalls);

        var toolService = new PlatformToolJobService(db, registry, priceSync, new NoopOpLogger(), actionExecutor, exec);
        var exportPricesEntry = new ToolCatalogEntry
        {
            Id = PlatformTools.ExportAllPricesId,
            Source = "platform",
            Mode = "job",
            Path = "/tools",
            Permissions = []
        };
        await toolService.RunAsync(exportPricesEntry, conn, 1, "tool-op-1", 7, null, CancellationToken.None);
        Assert.Equal(0, adapter.LegacyExportPricesCalls);
    }

    [Fact]
    public async Task ProductSync_SingleAndBulk_V1RoutesThroughCoordinator_WithIdenticalPayloadsAndAsyncProjection()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        await SeedCatalogAsync(db, "medusa");

        var adapter = new FakeAdapter("medusa", HostCaps.ExportProduct | HostCaps.ExportProductBulk);
        var registry = new SingleAdapterRegistry(adapter);
        var clock = new ManualClock();
        var commerce = new MarketplaceCommerceService(db, registry, new MarketplaceOrderSnapshotService(db, clock));
        var coordinator = new IntegrationCoordinator(db, new NoopOpLogger(), NullLogger<IntegrationCoordinator>.Instance);
        var exec = new MarketplaceCommerceExecutionService(
            coordinator,
            new IntegrationPreparationService(),
            key => key == "medusa" ? new MedusaPlugin() : null,
            new IntegrationExecutionOptions { Enabled = true, PublicApiSyncWaitMs = 0 },
            commerce);

        var sc = new ServiceCollection();
        sc.AddSingleton<Delegat.Infrastructure.Storage.IStorageService>(new FakeStorage());
        var sp = sc.BuildServiceProvider();
        var mappingSvc = new MappingService(db, sp);
        var productSync = new ProductSyncService(
            db, registry, mappingSvc, sp.GetRequiredService<Delegat.Infrastructure.Storage.IStorageService>(),
            NullLogger<ProductSyncService>.Instance, new NoopOpLogger(), quotaService: null!,
            mediaExportGrants: null, commerceExecution: exec);

        // Single export admits durable command (0 legacy calls) and creates NO premature Synced link.
        var single = await productSync.ExportProductAsync(101, 10, CancellationToken.None, authorUserId: 7, requestKey: "prod-single-101");
        Assert.True(single.IsPending, $"Expected IsPending=true, got IsSuccess={single.IsSuccess}, Error='{single.ErrorMessage}'");
        Assert.Equal(0, adapter.LegacyExportProductCalls);
        Assert.Empty(await db.productMarketplaceLinks.AsNoTracking().ToListAsync());

        // Bulk export admits durable command (0 legacy calls) and prepares identical payload for product 101.
        var bulk = await productSync.ExportProductsBulkAsync([101, 102], 10, CancellationToken.None, authorUserId: 7, requestKey: "prod-bulk-101-102");
        Assert.True(bulk.IsPending);
        Assert.Equal(0, adapter.LegacyExportBulkCalls);

        var singleCmd = await db.integrationCommands.AsNoTracking().SingleAsync(c => c.commandId == single.IntegrationCommandId);
        var bulkCmd = await db.integrationCommands.AsNoTracking().SingleAsync(c => c.commandId == bulk.IntegrationCommandId);
        var singleStage = await db.integrationStages.AsNoTracking().SingleAsync(s => s.commandId == singleCmd.Id && s.itemKey == "product:101");
        var bulkStage101 = await db.integrationStages.AsNoTracking().SingleAsync(s => s.commandId == bulkCmd.Id && s.itemKey == "product:101");
        Assert.Equal(singleStage.kind, bulkStage101.kind);
        Assert.Equal(singleStage.stageKey, bulkStage101.stageKey);
        Assert.Equal(singleStage.payloadJson, bulkStage101.payloadJson);

        // Drive worker on both commands -> worker projects confirmed ProductMarketplaceLink rows!
        var worker = BuildWorker(
            dsn, clock, new MedusaPlugin(), "medusa",
            new Dictionary<string, string> { ["apiBaseUrl"] = "https://medusa.example.com", ["apiKey"] = "med-key-1" },
            (req, _) =>
            {
                var extId = req.Body != null && req.Body.Contains("SKU-102") ? "med_prod_102" : "med_prod_101";
                return Task.FromResult(new IntegrationTransportResponse(
                    200, new Dictionary<string, string>(), $$"""{"success":true,"product_id":"{{extId}}"}"""));
            });

        await worker.ProcessDueStagesAsync(CancellationToken.None);
        // Second cycle picks up bulk product:101 which waited on cross-command same-entity ordering!
        clock.Now = clock.Now.AddSeconds(10);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        await using var verifyDb = TestDbFactory.CreateSqliteContext(dsn);
        var links = await verifyDb.productMarketplaceLinks.AsNoTracking().OrderBy(l => l.productId).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Equal("med_prod_101", links[0].externalProductId);
        Assert.Equal(ProductMarketplaceStatus.Synced, links[0].status);
        Assert.Equal("med_prod_102", links[1].externalProductId);
        Assert.Equal(ProductMarketplaceStatus.Synced, links[1].status);
    }

    [Fact]
    public async Task AllSevenAdapters_A01_SingleVsBulkAndOneItemTail_PrepareIdenticalStages()
    {
        var cases = new (string Name, IMarketplaceExecutionProvider Provider, IntegrationOperationKind OpKind, ConnectionContext Conn, List<IntegrationItemIntent> Items)[]
        {
            (
                "avito:price",
                new AvitoPlugin(),
                IntegrationOperationKind.PriceExport,
                new ConnectionContext { ConnectionId = 1, AdapterKey = "avito", Credentials = new() { ["clientId"] = "cid", ["clientSecret"] = "sec" } },
                [
                    new("av-1", new Dictionary<string, object?> { ["price"] = 1100m }, ExpectedRemoteId: "1001"),
                    new("av-2", new Dictionary<string, object?> { ["price"] = 2200m }, ExpectedRemoteId: "1002"),
                    new("av-3", new Dictionary<string, object?> { ["price"] = 3300m }, ExpectedRemoteId: "1003")
                ]
            ),
            (
                "medusa:product",
                new MedusaPlugin(),
                IntegrationOperationKind.ProductExport,
                new ConnectionContext { ConnectionId = 2, AdapterKey = "medusa", Credentials = new() { ["apiBaseUrl"] = "https://medusa.example", ["apiKey"] = "mk" } },
                [
                    new("m-1", new Dictionary<string, object?> { ["name"] = "A", ["sku"] = "M-1", ["price"] = 100m }),
                    new("m-2", new Dictionary<string, object?> { ["name"] = "B", ["sku"] = "M-2", ["price"] = 200m }),
                    new("m-3", new Dictionary<string, object?> { ["name"] = "C", ["sku"] = "M-3", ["price"] = 300m })
                ]
            ),
            (
                "opencart:product",
                new OpenCartPlugin(),
                IntegrationOperationKind.ProductExport,
                new ConnectionContext { ConnectionId = 3, AdapterKey = "opencart", Credentials = new() { ["apiBaseUrl"] = "https://oc.example/index.php?route=extension/delegat/api", ["apiKey"] = "ock" } },
                [
                    new("oc-1", new Dictionary<string, object?> { ["name"] = "A", ["sku"] = "OC-1", ["price"] = 100m }),
                    new("oc-2", new Dictionary<string, object?> { ["name"] = "B", ["sku"] = "OC-2", ["price"] = 200m }),
                    new("oc-3", new Dictionary<string, object?> { ["name"] = "C", ["sku"] = "OC-3", ["price"] = 300m })
                ]
            ),
            (
                "ozon:price",
                new OzonPlugin(),
                IntegrationOperationKind.PriceExport,
                new ConnectionContext { ConnectionId = 4, AdapterKey = "ozon", Credentials = new() { ["clientId"] = "123", ["apiKey"] = "ozk" } },
                [
                    new("oz-1", new Dictionary<string, object?> { ["price"] = 1000m }, ExpectedRemoteId: "501"),
                    new("oz-2", new Dictionary<string, object?> { ["price"] = 2000m }, ExpectedRemoteId: "502"),
                    new("oz-3", new Dictionary<string, object?> { ["price"] = 3000m }, ExpectedRemoteId: "503")
                ]
            ),
            (
                "wildberries:price",
                new WildberriesPlugin(),
                IntegrationOperationKind.PriceExport,
                new ConnectionContext { ConnectionId = 5, AdapterKey = "wildberries", Credentials = new() { ["apiKey"] = "wbk" } },
                [
                    new("wb-1", new Dictionary<string, object?> { ["price"] = 1000m }, ExpectedRemoteId: "601"),
                    new("wb-2", new Dictionary<string, object?> { ["price"] = 2000m }, ExpectedRemoteId: "602"),
                    new("wb-3", new Dictionary<string, object?> { ["price"] = 3000m }, ExpectedRemoteId: "603")
                ]
            ),
            (
                "woocommerce:product",
                new WooCommercePlugin(),
                IntegrationOperationKind.ProductExport,
                new ConnectionContext { ConnectionId = 6, AdapterKey = "woocommerce", Credentials = new() { ["siteUrl"] = "https://woo.example", ["apiKey"] = "wook" } },
                [
                    new("w-1", new Dictionary<string, object?> { ["name"] = "A", ["sku"] = "W-1", ["regular_price"] = "100" }),
                    new("w-2", new Dictionary<string, object?> { ["name"] = "B", ["sku"] = "W-2", ["regular_price"] = "200" }),
                    new("w-3", new Dictionary<string, object?> { ["name"] = "C", ["sku"] = "W-3", ["regular_price"] = "300" })
                ]
            ),
            (
                "yandex-market:price",
                new YandexMarketPlugin(),
                IntegrationOperationKind.PriceExport,
                new ConnectionContext { ConnectionId = 7, AdapterKey = "yandex-market", Credentials = new() { ["apiKey"] = "ymk", ["businessId"] = "999" } },
                [
                    new("ym-1", new Dictionary<string, object?> { ["price"] = 1000m, ["currency"] = "RUR" }, ExpectedRemoteId: "YM-1"),
                    new("ym-2", new Dictionary<string, object?> { ["price"] = 2000m, ["currency"] = "RUR" }, ExpectedRemoteId: "YM-2"),
                    new("ym-3", new Dictionary<string, object?> { ["price"] = 3000m, ["currency"] = "RUR" }, ExpectedRemoteId: "YM-3")
                ]
            )
        };

        foreach (var (name, provider, opKind, conn, items) in cases)
        {
            var singlePrep = await provider.PrepareAsync(new IntegrationPreparationInput(opKind, conn, [items[0]], null, $"cmd-{name}-single"));
            var bulkPrep = await provider.PrepareAsync(new IntegrationPreparationInput(opKind, conn, items, null, $"cmd-{name}-bulk"));
            var tailPrep = await provider.PrepareAsync(new IntegrationPreparationInput(opKind, conn, [items[2]], null, $"cmd-{name}-tail"));

            Assert.True(singlePrep.Issues.Count == 0, $"{name} single had issues: {string.Join("; ", singlePrep.Issues.Select(i => i.Code))}");
            Assert.True(bulkPrep.Issues.Count == 0, $"{name} bulk had issues: {string.Join("; ", bulkPrep.Issues.Select(i => i.Code))}");
            Assert.True(tailPrep.Issues.Count == 0, $"{name} tail had issues: {string.Join("; ", tailPrep.Issues.Select(i => i.Code))}");

            var singleStages = singlePrep.Items.Single(i => i.ItemKey == items[0].ItemKey).Stages;
            var bulkFirstStages = bulkPrep.Items.Single(i => i.ItemKey == items[0].ItemKey).Stages;
            Assert.Equal(singleStages.Count, bulkFirstStages.Count);
            for (var i = 0; i < singleStages.Count; i++)
            {
                Assert.Equal(singleStages[i].StageKey, bulkFirstStages[i].StageKey);
                Assert.Equal(singleStages[i].Kind, bulkFirstStages[i].Kind);
                Assert.Equal(singleStages[i].PayloadJson, bulkFirstStages[i].PayloadJson);
                Assert.Equal(singleStages[i].DependsOn, bulkFirstStages[i].DependsOn);
            }

            var tailStages = tailPrep.Items.Single(i => i.ItemKey == items[2].ItemKey).Stages;
            var bulkTailStages = bulkPrep.Items.Single(i => i.ItemKey == items[2].ItemKey).Stages;
            Assert.Equal(tailStages.Count, bulkTailStages.Count);
            for (var i = 0; i < tailStages.Count; i++)
            {
                Assert.Equal(tailStages[i].StageKey, bulkTailStages[i].StageKey);
                Assert.Equal(tailStages[i].Kind, bulkTailStages[i].Kind);
                Assert.Equal(tailStages[i].PayloadJson, bulkTailStages[i].PayloadJson);
                Assert.Equal(tailStages[i].DependsOn, bulkTailStages[i].DependsOn);
            }
        }
    }
}
