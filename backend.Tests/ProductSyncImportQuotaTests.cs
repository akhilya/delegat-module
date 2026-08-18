using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

[Collection("EnvVars")]
public class ProductSyncImportQuotaTests
{
    [Fact]
    public async Task Import_AtProductLimit_DoesNotCreateOrFetch()
    {
        using var _ = SetEnv("MAX_PRODUCTS", "1");
        var (svc, db, connection, adapter) = Create();
        var before = await db.products.CountAsync();

        var result = await svc.ImportProductAsync(connection.Id, "ext-new");

        Assert.False(result.IsSuccess);
        Assert.False(result.AlreadyLinked);
        Assert.Equal("product_limit_reached", result.ErrorCode);
        Assert.Equal(before, await db.products.CountAsync());
        Assert.Equal(0, adapter.ImportCalls);
    }

    [Fact]
    public async Task Import_UnderLimit_CreatesProduct()
    {
        using var _ = SetEnv("MAX_PRODUCTS", "2");
        var (svc, db, connection, adapter) = Create();

        var result = await svc.ImportProductAsync(connection.Id, "ext-new");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, await db.products.CountAsync());
        Assert.Equal(1, adapter.ImportCalls);
    }

    [Fact]
    public async Task Import_AlreadyLinkedAtLimit_DoesNotCreateAnother()
    {
        using var _ = SetEnv("MAX_PRODUCTS", "1");
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        data.AddProductLink(connection.Id, product.Id, "ext-1");
        await data.SaveAsync();

        var adapter = new CountingImportAdapter(["ext-1"]);
        var svc = CreateService(db, adapter);

        var result = await svc.ImportProductAsync(connection.Id, "ext-1");

        Assert.True(result.AlreadyLinked);
        Assert.Equal(1, await db.products.CountAsync());
        Assert.Equal(0, adapter.ImportCalls);
    }

    private static (
        ProductSyncService svc,
        DataContext db,
        MarketplaceConnection connection,
        CountingImportAdapter adapter) Create()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        data.AddProduct(store.Id);
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        db.SaveChanges();

        var adapter = new CountingImportAdapter(["ext-new"]);
        return (CreateService(db, adapter), db, connection, adapter);
    }

    private static ProductSyncService CreateService(DataContext db, IMarketplaceAdapter adapter)
    {
        var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), db);
        return new ProductSyncService(
            db,
            new SingleAdapterRegistry(adapter),
            mappingService: null!,
            storageService: null!,
            NullLogger<ProductSyncService>.Instance,
            new NoopOperationLogger(),
            quota);
    }

    private static IDisposable SetEnv(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestore(name, previous);
    }

    private sealed class EnvRestore(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }

    private sealed class SingleAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public IMarketplaceAdapter? GetByKey(string key) =>
            key == adapter.Key ? adapter : null;
        public bool Exists(string key) => key == adapter.Key;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null)
            => Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }

    private sealed class CountingImportAdapter(IReadOnlyCollection<string> foundIds) : IMarketplaceAdapter
    {
        private readonly HashSet<string> _found = new(foundIds, StringComparer.OrdinalIgnoreCase);
        public int ImportCalls { get; private set; }

        public string Key => "fake";
        public string DisplayName => "Fake";
        public string? Description => null;
        public string? LogoUrl => null;
        public MarketplaceCapabilities Capabilities => MarketplaceCapabilities.ImportProduct;
        public string? ProductEditUrlTemplate => null;

        public CredentialsSchema GetCredentialsSchema() => new();
        public RateLimitConfig? GetRateLimitConfig() => null;
        public Task<List<QuotaInfo>> GetQuotasAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new List<QuotaInfo>());
        public MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection) => null;
        public int ClearMetadataCaches(MarketplaceConnection connection) => 0;
        public Task<ConnectionTestResult> TestConnectionAsync(MarketplaceConnection connection, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true));
        public Task<List<ExternalCategory>> GetCategoriesAsync(MarketplaceConnection connection, string? parentId = null, string? searchQuery = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<ExternalField>> GetCategoryFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<ExternalField>> GetPriceFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => Task.FromResult(new List<ExternalField>());
        public Task<CreateCategoryResult> CreateCategoryAsync(MarketplaceConnection connection, CreateCategoryRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<ExternalProduct?> ImportProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
        {
            ImportCalls++;
            return Task.FromResult(_found.Contains(externalProductId)
                ? new ExternalProduct(externalProductId, "SKU-1", "Imported")
                : null);
        }

        public Task<List<ExternalProduct>> ImportProductsBatchAsync(MarketplaceConnection connection, List<string> externalProductIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, string? offerIdFilter = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> ExportProductAsync(MarketplaceConnection connection, Dictionary<string, object> productData, string? existingExternalId = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<BulkExportResult> ExportProductsBulkAsync(MarketplaceConnection connection, List<BulkExportItem> items, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> UpdateSkuAsync(MarketplaceConnection connection, string externalProductId, string newSku, string? currentSku = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> ArchiveProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<PriceInfo>> ImportPricesAsync(MarketplaceConnection connection, List<string>? externalProductIds = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ImportResult> ExportPricesAsync(MarketplaceConnection connection, List<PriceInfo> prices, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<StockInfo>> ImportStocksAsync(MarketplaceConnection connection, List<string>? externalProductIds = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ImportResult> ExportStocksAsync(MarketplaceConnection connection, List<StockInfo> stocks, CancellationToken ct = default)
            => throw new NotImplementedException();
        public FieldBasedGroupingConfig? GetFieldBasedGroupingConfig() => null;
        public Task<CreateGroupResult> CreateGroupAsync(MarketplaceConnection connection, List<string> productExternalIds, string? groupName = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<GroupMembershipResult> AddProductsToGroupAsync(MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<GroupMembershipResult> RemoveProductsFromGroupAsync(MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExportResult> DeleteGroupAsync(MarketplaceConnection connection, string externalGroupId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PagedResult<ExternalGroup>> ImportGroupsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
