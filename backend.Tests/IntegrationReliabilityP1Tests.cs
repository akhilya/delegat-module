using System.Net;
using System.Reflection;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Tests.Fakes;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HostBulkExportResult = Delegat.Models.Marketplace.DTOs.BulkExportResult;
using HostExportItemResult = Delegat.Models.Marketplace.DTOs.ExportItemResult;
using HostExportResult = Delegat.Models.Marketplace.DTOs.ExportResult;
using HostImportResult = Delegat.Models.Marketplace.DTOs.ImportResult;
using HostImportError = Delegat.Models.Marketplace.DTOs.ImportError;
using HostMarketplaceCapabilities = Delegat.Services.Marketplace.MarketplaceCapabilities;
using SdkBulkExportResult = Delegat.Marketplace.SDK.DTOs.BulkExportResult;
using SdkExportResult = Delegat.Marketplace.SDK.DTOs.ExportResult;
using SdkMarketplaceCapabilities = Delegat.Marketplace.SDK.MarketplaceCapabilities;
using SdkConnectionTestResult = Delegat.Marketplace.SDK.DTOs.ConnectionTestResult;
using SdkCredentialsSchema = Delegat.Marketplace.SDK.CredentialsSchema;

namespace Delegat.Tests;

public sealed class IntegrationReliabilityP1Tests
{
    [Fact]
    public async Task SdkBulkFallback_PreservesDetailsAndRetryableSemantics()
    {
        var plugin = new SemanticPlugin
        {
            SingleResult = new SdkExportResult(
                false,
                ErrorMessage: "provider rejected",
                Details: new Dictionary<string, object> { ["taskId"] = "task-1" },
                Retryable: false)
        };

        var result = await plugin.ExportProductsBulkAsync(
            new ConnectionContext(),
            [new Delegat.Marketplace.SDK.DTOs.BulkExportItem(7, new Dictionary<string, object>())]);

        var item = Assert.Single(result.Items!);
        Assert.False(item.Success);
        Assert.False(item.Retryable);
        Assert.Equal("task-1", item.Details?["taskId"]);
    }

    [Fact]
    public async Task SdkBulkFallback_PropagatesCallerCancellation()
    {
        var plugin = new SemanticPlugin();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => plugin.ExportProductsBulkAsync(
            new ConnectionContext(),
            [new Delegat.Marketplace.SDK.DTOs.BulkExportItem(7, new Dictionary<string, object>())],
            cts.Token));
    }

    [Fact]
    public async Task AdapterHttpLog_MasksDelegatBridgeKeyAndCarriesOperationContext()
    {
        var sink = new CapturingHttpLogger();
        var context = new ConnectionContext
        {
            StoreId = 1,
            ConnectionId = 2,
            AdapterKey = "opencart",
            ExecutionContext = new IntegrationAttemptContext(
                OperationId: "operation-1",
                CommandId: "command-1",
                ItemKey: "product:1",
                StageKey: "card",
                AttemptNumber: 1,
                RequestKey: "request-1")
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://shop.example.test/api");
        request.Headers.TryAddWithoutValidation("X-Delegat-Api-Key", "bridge-secret");
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        await AdapterHttpLogHelper.LogExchangeAsync(
            sink, context, request, response, "{}", "{}", 12);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal("operation-1", entry.OperationId);
        Assert.Contains("\"X-Delegat-Api-Key\":\"***\"", entry.RequestHeadersJson);
        Assert.DoesNotContain("bridge-secret", entry.RequestHeadersJson);
    }

    [Fact]
    public void HostHttpLogRedaction_MasksDelegatBridgeKey()
    {
        var json = AdapterHttpLogService.RedactHeadersJson(
        [
            new KeyValuePair<string, IEnumerable<string>>(
                "X-Delegat-Api-Key", ["bridge-secret"])
        ]);

        Assert.Contains("\"X-Delegat-Api-Key\":\"***\"", json);
        Assert.DoesNotContain("bridge-secret", json);
    }

    [Fact]
    public async Task SingleExport_FailedPriceFollowUp_DoesNotReturnSuccess()
    {
        var (db, product, connection, adapter) = await CreateScenarioAsync();
        adapter.CapabilitiesValue = HostMarketplaceCapabilities.ExportProduct
            | HostMarketplaceCapabilities.ExportPrices
            | HostMarketplaceCapabilities.ExportStocks;
        adapter.SingleResult = new HostExportResult(true, "remote-1");
        adapter.PriceResult = new HostImportResult(
            1, 0, 1,
            [new HostImportError("remote-1", "price rejected")]);
        adapter.StockResult = new HostImportResult(1, 1, 0);
        var service = CreateProductSyncService(db, adapter);

        var result = await service.ExportProductAsync(product.Id, connection.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("Price export failed", result.ErrorMessage);
        Assert.Contains("price rejected", result.ErrorMessage);
        var link = await db.productMarketplaceLinks.SingleAsync();
        Assert.Equal("remote-1", link.externalProductId);
    }

    [Fact]
    public async Task BulkExport_MissingAdapterItemResult_IsSafeNonRetryableFailure()
    {
        var (db, product, connection, adapter) = await CreateScenarioAsync();
        adapter.CapabilitiesValue = HostMarketplaceCapabilities.ExportProduct
            | HostMarketplaceCapabilities.ExportProductBulk;
        adapter.BulkResult = new HostBulkExportResult(1, 0, 0, []);
        var service = CreateProductSyncService(db, adapter);

        var result = await service.ExportProductsBulkAsync([product.Id], connection.Id);

        var item = Assert.Single(result.Items);
        Assert.False(item.IsSuccess);
        Assert.False(item.Retryable);
        Assert.Contains("did not return a result", item.ErrorMessage);
        Assert.Empty(await db.productMarketplaceLinks.ToListAsync());
    }

    [Fact]
    public async Task BulkExport_DuplicateAdapterItemResult_IsSafeNonRetryableFailure()
    {
        var (db, product, connection, adapter) = await CreateScenarioAsync();
        adapter.CapabilitiesValue = HostMarketplaceCapabilities.ExportProduct
            | HostMarketplaceCapabilities.ExportProductBulk;
        adapter.BulkResult = new HostBulkExportResult(
            1, 1, 0,
            [
                new HostExportItemResult(product.Id, true, "remote-1"),
                new HostExportItemResult(product.Id, false, ErrorMessage: "conflict")
            ]);
        var service = CreateProductSyncService(db, adapter);

        var result = await service.ExportProductsBulkAsync([product.Id], connection.Id);

        var item = Assert.Single(result.Items);
        Assert.False(item.IsSuccess);
        Assert.False(item.Retryable, item.ErrorMessage);
        Assert.Contains("conflicting results", item.ErrorMessage);
        Assert.Empty(await db.productMarketplaceLinks.ToListAsync());
    }

    [Fact]
    public async Task BulkExport_ForeignAdapterItemResult_InvalidatesWholeResponse()
    {
        var (db, product, connection, adapter) = await CreateScenarioAsync();
        adapter.CapabilitiesValue = HostMarketplaceCapabilities.ExportProduct
            | HostMarketplaceCapabilities.ExportProductBulk;
        adapter.BulkResult = new HostBulkExportResult(
            1, 1, 0,
            [
                new HostExportItemResult(product.Id, true, "remote-1"),
                new HostExportItemResult(9999, true, "foreign-remote")
            ]);
        var service = CreateProductSyncService(db, adapter);

        var result = await service.ExportProductsBulkAsync([product.Id], connection.Id);

        var item = Assert.Single(result.Items);
        Assert.False(item.IsSuccess);
        Assert.False(item.Retryable, item.ErrorMessage);
        Assert.Contains("unrequested products", item.ErrorMessage);
        Assert.Empty(await db.productMarketplaceLinks.ToListAsync());
    }

    [Fact]
    public async Task ExportWorker_RetryableFirstFailure_KeepsOperationRunningUntilRetry()
    {
        var (db, product, connection, adapter) = await CreateScenarioAsync();
        adapter.CapabilitiesValue = HostMarketplaceCapabilities.ExportProduct;
        adapter.SingleResult = new HostExportResult(false, ErrorMessage: "temporary marketplace failure");
        var operationLogger = new RecordingOperationLogger();
        var worker = CreateWorker(db, adapter, operationLogger);
        var operation = new TestDataBuilder(db).AddOperation(connection.storeId, operationId: "export-op");
        db.pendingProductExports.Add(new PendingProductExport
        {
            connectionId = connection.Id,
            productId = product.Id,
            productName = product.name,
            storeId = connection.storeId,
            batchId = operation.operationId,
            status = ProductExportStatus.Pending
        });
        await db.SaveChangesAsync();

        await worker.ProcessPendingExportsAsync(CancellationToken.None);

        var row = await db.pendingProductExports.SingleAsync();
        Assert.Equal(ProductExportStatus.Failed, row.status);
        Assert.Equal(1, row.retryCount);
        Assert.Empty(operationLogger.Finishes);

        adapter.SingleResult = new HostExportResult(true, "remote-1");
        await worker.ProcessPendingExportsAsync(CancellationToken.None);

        row = await db.pendingProductExports.SingleAsync();
        Assert.Equal(ProductExportStatus.Completed, row.status);
        var finish = Assert.Single(operationLogger.Finishes);
        Assert.Equal("completed", finish.Status);
    }

    [Fact]
    public async Task ExportWorker_DifferentOperationsOnSameProduct_DoNotShareBulkCall()
    {
        var (db, product, connection, adapter) = await CreateScenarioAsync();
        adapter.CapabilitiesValue = HostMarketplaceCapabilities.ExportProduct
            | HostMarketplaceCapabilities.ExportProductBulk;
        adapter.SingleResult = new HostExportResult(true, "remote-1");
        var operationLogger = new RecordingOperationLogger();
        var worker = CreateWorker(db, adapter, operationLogger);
        var builder = new TestDataBuilder(db);
        var first = builder.AddOperation(connection.storeId, operationId: "op-first");
        var second = builder.AddOperation(connection.storeId, operationId: "op-second");
        db.pendingProductExports.AddRange(
            new PendingProductExport
            {
                connectionId = connection.Id,
                productId = product.Id,
                productName = product.name,
                storeId = connection.storeId,
                batchId = first.operationId,
                status = ProductExportStatus.Pending
            },
            new PendingProductExport
            {
                connectionId = connection.Id,
                productId = product.Id,
                productName = product.name,
                storeId = connection.storeId,
                batchId = second.operationId,
                status = ProductExportStatus.Pending
            });
        await db.SaveChangesAsync();

        await worker.ProcessPendingExportsAsync(CancellationToken.None);

        Assert.Equal(2, adapter.SingleCalls);
        Assert.Empty(adapter.BulkCalls);
        Assert.Equal(2, operationLogger.Finishes.Count);
        Assert.All(await db.pendingProductExports.ToListAsync(),
            row => Assert.Equal(ProductExportStatus.Completed, row.status));
    }

    private static async Task<(DataContext Db, Models.Products.Product Product, Delegat.Models.Marketplace.MarketplaceConnection Connection, P1AdapterProxy Adapter)>
        CreateScenarioAsync()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore(members: [user]);
        var category = data.AddCategory(store.Id);
        var product = data.AddProduct(store.Id, categoryId: category.Id, price: 120m);
        var connection = data.AddConnection(store.Id, adapterKey: "p1-fake");
        var mapping = data.AddMapping(connection.Id, category.Id);
        mapping.graphJson = GraphGenerator.BuildGraphJson([]);
        await data.SaveAsync();
        var adapter = DispatchProxy.Create<IMarketplaceAdapter, P1AdapterProxy>();
        return (db, product, connection, (P1AdapterProxy)(object)adapter);
    }

    private static ProductSyncService CreateProductSyncService(
        DataContext db,
        P1AdapterProxy adapter)
    {
        var storage = new RecordingCatalogStorage();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IStorageService>(storage)
            .BuildServiceProvider();
        var mapping = new MappingService(db, serviceProvider);
        var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), db);
        return new ProductSyncService(
            db,
            new SingleAdapterRegistry((IMarketplaceAdapter)(object)adapter),
            mapping,
            storage,
            NullLogger<ProductSyncService>.Instance,
            new NoopOperationLogger(),
            quota,
            mediaExportGrants: null);
    }

    private static ProductExportBackgroundService CreateWorker(
        DataContext db,
        P1AdapterProxy adapter,
        RecordingOperationLogger operationLogger)
    {
        var storage = new RecordingCatalogStorage();
        var mappingProvider = new ServiceCollection()
            .AddSingleton<IStorageService>(storage)
            .BuildServiceProvider();
        var serviceProvider = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IStorageService>(storage)
            .AddSingleton<IMarketplaceAdapterRegistry>(
                new SingleAdapterRegistry((IMarketplaceAdapter)(object)adapter))
            .AddSingleton(new MappingService(db, mappingProvider))
            .AddSingleton(new QuotaService(TestDbFactory.CreateUnitOfWork(db), db))
            .AddSingleton<ProductSyncService>(sp => new ProductSyncService(
                db,
                sp.GetRequiredService<IMarketplaceAdapterRegistry>(),
                sp.GetRequiredService<MappingService>(),
                sp.GetRequiredService<IStorageService>(),
                NullLogger<ProductSyncService>.Instance,
                new NoopOperationLogger(),
                sp.GetRequiredService<QuotaService>(),
                mediaExportGrants: null))
            .BuildServiceProvider();

        return new ProductExportBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ProductExportBackgroundService>.Instance,
            operationLogger);
    }

    private sealed class SemanticPlugin : MarketplacePluginBase
    {
        public SdkExportResult SingleResult { get; set; } = new(true, "remote-1");

        public override string Key => "semantic";
        public override string DisplayName => "Semantic";
        public override string Version => "1";
        public override SdkMarketplaceCapabilities Capabilities => SdkMarketplaceCapabilities.ExportProduct;
        public override SdkCredentialsSchema GetCredentialsSchema() => new();
        public override Task<SdkConnectionTestResult> TestConnectionAsync(
            ConnectionContext context, CancellationToken ct = default) =>
            Task.FromResult(new SdkConnectionTestResult(true));

        public override Task<SdkExportResult> ExportProductAsync(
            ConnectionContext context,
            Dictionary<string, object> productData,
            string? existingExternalId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(SingleResult);
        }
    }

    public class P1AdapterProxy : DispatchProxy
    {
        public HostMarketplaceCapabilities CapabilitiesValue { get; set; } = HostMarketplaceCapabilities.ExportProduct;
        public HostExportResult SingleResult { get; set; } = new(true, "remote-1");
        public HostBulkExportResult BulkResult { get; set; } = new(0, 0, 0, []);
        public HostImportResult PriceResult { get; set; } = new(1, 1, 0);
        public HostImportResult StockResult { get; set; } = new(1, 1, 0);
        public int SingleCalls { get; private set; }
        public List<List<Delegat.Models.Marketplace.DTOs.BulkExportItem>> BulkCalls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_Key" => "p1-fake",
                "get_DisplayName" => "P1 fake",
                "get_Capabilities" => CapabilitiesValue,
                "GetFieldBasedGroupingConfig" => null,
                "ExportProductAsync" => ExportSingle(),
                "ExportProductsBulkAsync" => ExportBulk(args),
                "ExportPricesAsync" => Task.FromResult(PriceResult),
                "ExportStocksAsync" => Task.FromResult(StockResult),
                _ => throw new NotSupportedException($"Unexpected adapter call: {targetMethod?.Name}")
            };
        }

        private Task<HostExportResult> ExportSingle()
        {
            SingleCalls++;
            return Task.FromResult(SingleResult);
        }

        private Task<HostBulkExportResult> ExportBulk(object?[]? args)
        {
            BulkCalls.Add(Assert.IsType<List<Delegat.Models.Marketplace.DTOs.BulkExportItem>>(args![1]));
            return Task.FromResult(BulkResult);
        }
    }

    private sealed class CapturingHttpLogger : IAdapterHttpLogger
    {
        public List<AdapterHttpLogEntry> Entries { get; } = [];
        public void Log(AdapterHttpLogEntry entry) => Entries.Add(entry);
    }

    private sealed class NoopOperationLogger : IOperationLogger
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

    private sealed class RecordingOperationLogger : IOperationLogger
    {
        public List<(string OperationId, string Status)> Finishes { get; } = [];

        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null)
            => Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null)
        {
            Finishes.Add((operationId, status));
            return Task.CompletedTask;
        }
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }
}
