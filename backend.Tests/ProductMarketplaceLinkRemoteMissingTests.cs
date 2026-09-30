using Delegat.Enums;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class ProductMarketplaceLinkRemoteMissingTests
{
    private static (DataContext db, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db));
    }

    [Fact]
    public async Task Link_CanPersistRemoteMissingStatus()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id, "Crash");
        var connection = data.AddConnection(store.Id);
        data.AddProductLink(connection.Id, product.Id, "144", ProductMarketplaceStatus.RemoteMissing);
        await data.SaveAsync();

        var link = await db.productMarketplaceLinks.SingleAsync();
        Assert.Equal(ProductMarketplaceStatus.RemoteMissing, link.status);
        Assert.Equal("144", link.externalProductId);
    }

    [Fact]
    public async Task UpdateFill_MarksRemoteMissingWithError()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var link = data.AddProductLink(
            data.AddConnection(store.Id).Id,
            data.AddProduct(store.Id).Id,
            "99",
            ProductMarketplaceStatus.Synced);
        await data.SaveAsync();

        link.UpdateFill(new ProductMarketplaceLink.UpdateForm
        {
            status = ProductMarketplaceStatus.RemoteMissing,
            lastErrorMessage = "Product '99' not found on the marketplace",
            lastSyncAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var saved = await db.productMarketplaceLinks.SingleAsync();
        Assert.Equal(ProductMarketplaceStatus.RemoteMissing, saved.status);
        Assert.Contains("not found", saved.lastErrorMessage);
    }

    [Fact]
    public async Task ExportEligible_IncludesRemoteMissing_ExcludesSyncedAndModified()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var connection = data.AddConnection(store.Id);
        data.AddMapping(connection.Id, category.Id);

        var synced = data.AddProduct(store.Id, "Synced", categoryId: category.Id);
        var broken = data.AddProduct(store.Id, "Broken", categoryId: category.Id);
        var modified = data.AddProduct(store.Id, "Modified", categoryId: category.Id);
        var errored = data.AddProduct(store.Id, "Error", categoryId: category.Id);

        data.AddProductLink(connection.Id, synced.Id, "1", ProductMarketplaceStatus.Synced);
        data.AddProductLink(connection.Id, broken.Id, "2", ProductMarketplaceStatus.RemoteMissing);
        data.AddProductLink(connection.Id, modified.Id, "3", ProductMarketplaceStatus.Modified);
        data.AddProductLink(connection.Id, errored.Id, "4", ProductMarketplaceStatus.SyncError);
        await data.SaveAsync();

        // Mirrors GetLocalProductsForExport linked-id exclusion.
        var linkedProductIds = await db.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => l.connectionId == connection.Id
                && (l.status == ProductMarketplaceStatus.Synced
                    || l.status == ProductMarketplaceStatus.Modified))
            .Select(l => l.productId)
            .ToListAsync();

        var eligibleIds = await db.products
            .AsNoTracking()
            .Where(p => p.storeId == store.Id
                && p.categoryId == category.Id
                && !linkedProductIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();

        Assert.Contains(broken.Id, eligibleIds);
        Assert.Contains(errored.Id, eligibleIds);
        Assert.DoesNotContain(synced.Id, eligibleIds);
        Assert.DoesNotContain(modified.Id, eligibleIds);
    }

    [Fact]
    public async Task VerifyLinks_MarksMissingAsRemoteMissing_AndRecoversFound()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var alive = data.AddProduct(store.Id, "Alive");
        var dead = data.AddProduct(store.Id, "Dead");
        var recovered = data.AddProduct(store.Id, "Recovered");

        data.AddProductLink(connection.Id, alive.Id, "alive-1", ProductMarketplaceStatus.Synced);
        data.AddProductLink(connection.Id, dead.Id, "dead-1", ProductMarketplaceStatus.Synced);
        data.AddProductLink(connection.Id, recovered.Id, "back-1", ProductMarketplaceStatus.RemoteMissing);
        await data.SaveAsync();

        var adapter = new FakeImportAdapter(foundIds: ["alive-1", "back-1"]);
        var registry = new FakeAdapterRegistry(adapter);
        var service = new ProductSyncService(
            db,
            registry,
            mappingService: null!,
            storageService: null!,
            NullLogger<ProductSyncService>.Instance,
            operationLogger: null!,
            new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));

        var result = await service.VerifyLinksAsync(connection.Id);

        Assert.Equal(3, result.Checked);
        Assert.Equal(2, result.Confirmed);
        Assert.Equal(1, result.Broken);
        Assert.Equal(1, result.Recovered);

        var links = await db.productMarketplaceLinks.ToDictionaryAsync(l => l.externalProductId);
        Assert.Equal(ProductMarketplaceStatus.Synced, links["alive-1"].status);
        Assert.Equal(ProductMarketplaceStatus.RemoteMissing, links["dead-1"].status);
        Assert.Equal(ProductMarketplaceStatus.Synced, links["back-1"].status);
        Assert.Null(links["back-1"].lastErrorMessage);
        Assert.Contains("not found", links["dead-1"].lastErrorMessage!);
    }

    [Fact]
    public async Task VerifyLinks_SingleLink_OnlyChecksThatLink()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var a = data.AddProduct(store.Id, "A");
        var b = data.AddProduct(store.Id, "B");
        var linkA = data.AddProductLink(connection.Id, a.Id, "a-1", ProductMarketplaceStatus.Synced);
        data.AddProductLink(connection.Id, b.Id, "b-1", ProductMarketplaceStatus.Synced);
        await data.SaveAsync();

        var adapter = new FakeImportAdapter(foundIds: []);
        var service = new ProductSyncService(
            db,
            new FakeAdapterRegistry(adapter),
            null!,
            null!,
            NullLogger<ProductSyncService>.Instance,
            null!,
            new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));

        var result = await service.VerifyLinksAsync(connection.Id, linkA.Id);

        Assert.Equal(1, result.Checked);
        Assert.Equal(1, result.Broken);

        var links = await db.productMarketplaceLinks.ToDictionaryAsync(l => l.externalProductId);
        Assert.Equal(ProductMarketplaceStatus.RemoteMissing, links["a-1"].status);
        Assert.Equal(ProductMarketplaceStatus.Synced, links["b-1"].status);
    }

    [Fact]
    public async Task VerifyLinks_WholeConnection_ComparesOneCatalogList()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var alive = data.AddProduct(store.Id, "Alive");
        var dead = data.AddProduct(store.Id, "Dead");
        var recovered = data.AddProduct(store.Id, "Recovered");
        data.AddProductLink(connection.Id, alive.Id, "alive-1", ProductMarketplaceStatus.Synced);
        data.AddProductLink(connection.Id, dead.Id, "dead-1", ProductMarketplaceStatus.Synced);
        data.AddProductLink(connection.Id, recovered.Id, "back-1", ProductMarketplaceStatus.RemoteMissing);
        await data.SaveAsync();

        var adapter = new FakeImportAdapter(
            foundIds: [],
            catalogPages: [["alive-1"], ["back-1", "extra"]]);
        var service = new ProductSyncService(
            db,
            new FakeAdapterRegistry(adapter),
            null!,
            null!,
            NullLogger<ProductSyncService>.Instance,
            null!,
            new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));

        var result = await service.VerifyLinksAsync(connection.Id);

        Assert.Equal(0, adapter.BatchCalls);
        Assert.Equal(2, adapter.ListCalls);
        Assert.Equal(3, result.Checked);
        Assert.Equal(2, result.Confirmed);
        Assert.Equal(1, result.Broken);
        Assert.Equal(1, result.Recovered);

        var links = await db.productMarketplaceLinks.ToDictionaryAsync(l => l.externalProductId);
        Assert.Equal(ProductMarketplaceStatus.Synced, links["alive-1"].status);
        Assert.Equal(ProductMarketplaceStatus.RemoteMissing, links["dead-1"].status);
        Assert.Equal(ProductMarketplaceStatus.Synced, links["back-1"].status);
    }

    [Fact]
    public async Task VerifyLinks_SingleLink_DoesNotListTheCatalog()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var a = data.AddProduct(store.Id, "A");
        var linkA = data.AddProductLink(connection.Id, a.Id, "a-1", ProductMarketplaceStatus.Synced);
        await data.SaveAsync();

        var adapter = new FakeImportAdapter(foundIds: ["a-1"], catalogPages: [["a-1", "other"]]);
        var service = new ProductSyncService(
            db,
            new FakeAdapterRegistry(adapter),
            null!,
            null!,
            NullLogger<ProductSyncService>.Instance,
            null!,
            new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));

        var result = await service.VerifyLinksAsync(connection.Id, linkA.Id);

        Assert.Equal(1, result.Confirmed);
        Assert.Equal(0, adapter.ListCalls);
        Assert.Equal(1, adapter.BatchCalls);
    }

    private sealed class FakeAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public IMarketplaceAdapter? GetByKey(string key) =>
            key == adapter.Key ? adapter : null;
        public bool Exists(string key) => key == adapter.Key;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private sealed class FakeImportAdapter : IMarketplaceAdapter
    {
        private readonly HashSet<string> _found;
        private readonly IReadOnlyList<string[]>? _pages;

        public FakeImportAdapter(
            IReadOnlyCollection<string> foundIds,
            IReadOnlyList<string[]>? catalogPages = null)
        {
            _found = new HashSet<string>(foundIds, StringComparer.OrdinalIgnoreCase);
            _pages = catalogPages;
        }

        public int BatchCalls { get; private set; }
        public int ListCalls { get; private set; }

        public string Key => "fake";
        public string DisplayName => "Fake";
        public string? Description => null;
        public string? LogoUrl => null;
        public MarketplaceCapabilities Capabilities =>
            _pages == null
                ? MarketplaceCapabilities.ImportProduct
                : MarketplaceCapabilities.ImportProduct | MarketplaceCapabilities.ImportAllProducts;
        public string? ProductEditUrlTemplate => null;

        public CredentialsSchema GetCredentialsSchema() => new();
        public RateLimitConfig? GetRateLimitConfig() => null;
        public Task<List<Delegat.Marketplace.SDK.RateLimit.QuotaInfo>> GetQuotasAsync(
            MarketplaceConnection connection,
            CancellationToken ct = default)
            => Task.FromResult(new List<Delegat.Marketplace.SDK.RateLimit.QuotaInfo>());
        public MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection)
            => null;
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
            => Task.FromResult(_found.Contains(externalProductId)
                ? new ExternalProduct(externalProductId, null, externalProductId)
                : null);

        public Task<List<ExternalProduct>> ImportProductsBatchAsync(
            MarketplaceConnection connection,
            List<string> externalProductIds,
            CancellationToken ct = default)
        {
            BatchCalls++;
            return Task.FromResult(externalProductIds
                .Where(_found.Contains)
                .Select(id => new ExternalProduct(id, null, id))
                .ToList());
        }

        public Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, string? offerIdFilter = null, CancellationToken ct = default)
        {
            ListCalls++;
            if (_pages == null)
                throw new NotImplementedException();

            var index = 0;
            if (!string.IsNullOrEmpty(cursor))
                index = int.Parse(cursor);
            var ids = index < _pages.Count ? _pages[index] : [];
            var hasMore = index + 1 < _pages.Count;
            return Task.FromResult(new PagedResult<ExternalProduct>(
                ids.Select(id => new ExternalProduct(id, null, id)).ToList(),
                hasMore,
                hasMore ? (index + 1).ToString() : null));
        }
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
