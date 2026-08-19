using Delegat;
using Delegat.Enums;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

[Collection("EnvVars")]
public class CatalogScrapeServiceTests
{
    [Fact]
    public async Task Apply_CreatesProductWithoutMarketplaceLink_AndQueuesImages()
    {
        var (svc, db, connection, user, adapter) = Create();
        adapter.Add(new ExternalProduct(
            "nm-1",
            "ART-NEW",
            "Public tee",
            Price: 10m,
            ImageUrls: ["https://cdn.example/a.webp"],
            Variants: [new ExternalProductVariant("v1", "M", "46")]));

        var result = await svc.ApplyAsync(connection, ["nm-1"], user.Id, CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Failed);
        var product = Assert.Single(db.products.Where(p => p.name == "Public tee"));
        Assert.Equal("Public tee", product.name);
        Assert.Empty(db.productMarketplaceLinks);
        Assert.Empty(db.productVariantLinks);
        Assert.Single(db.productVariants);
        Assert.Equal(1, await db.pendingImageDownloads.CountAsync());
        Assert.Equal(1, adapter.GetPublicCalls);
        Assert.Equal(0, adapter.ImportCalls);
    }

    [Fact]
    public async Task Apply_CollidingSku_DoesNotReuseExistingArticle()
    {
        var (svc, db, connection, user, adapter) = Create(existingSku: "ART-1");
        adapter.Add(new ExternalProduct("9", "ART-1", "Copy"));

        var result = await svc.ApplyAsync(connection, ["9"], user.Id, CancellationToken.None);

        Assert.True(result.Imported == 1, $"imported={result.Imported} failed={result.Failed}");
        var skus = await db.products.Select(p => p.sku).ToListAsync();
        Assert.Equal(2, skus.Count);
        Assert.Contains("ART-1", skus);
        Assert.DoesNotContain(skus, s => s != "ART-1" && s == "ART-1");
        Assert.Contains(skus, s => s != "ART-1");
    }

    [Fact]
    public async Task Apply_QuotaStop_SkipsRestOfBatch()
    {
        using var _ = SetEnv("MAX_PRODUCTS", "2");
        var (svc, db, connection, user, adapter) = Create(existingSku: "KEEP");
        adapter.Add(new ExternalProduct("a", "A", "One"));
        adapter.Add(new ExternalProduct("b", "B", "Two"));

        var result = await svc.ApplyAsync(connection, ["a", "b"], user.Id, CancellationToken.None);

        Assert.True(result.QuotaStopped);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(2, await db.products.CountAsync());
        Assert.Empty(db.productMarketplaceLinks);
    }

    [Fact]
    public async Task Apply_StoppedOperation_DoesNotImportCards()
    {
        var (svc, db, connection, user, adapter) = Create();
        adapter.Add(new ExternalProduct("nm-1", "ART-NEW", "Public tee"));
        db.operations.Add(new Operation
        {
            operationId = "scrape-stop",
            storeId = connection.storeId,
            authorUserId = user.Id,
            type = "catalog-scrape",
            title = "Copy public catalog cards",
            status = "cancelled",
            createdAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await svc.ApplyAsync(
            connection, ["nm-1"], user.Id, CancellationToken.None, "scrape-stop");

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, adapter.GetPublicCalls);
        Assert.Empty(db.products.Where(p => p.name == "Public tee"));
    }

    [Fact]
    public async Task Apply_WhenMappingExists_AssignsCategoryAndAttributes()
    {
        var (svc, db, connection, user, adapter) = Create();
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "T-shirts");
        var brandAttr = data.AddCategoryAttribute(category.Id, "Brand", key: "brand");
        var colorAttr = data.AddCategoryAttribute(category.Id, "Color", key: "color");
        var mapping = data.AddMapping(connection.Id, category.Id, "42", "T-shirts");
        mapping.graphJson = GraphGenerator.BuildGraphJson(
        [
            new FieldSpec
            {
                externalFieldKey = "brand",
                externalFieldName = "Brand",
                externalFieldType = "string",
                sourceType = FieldMappingSourceType.CategoryAttribute,
                sourceAttributeId = brandAttr.Id,
            },
            new FieldSpec
            {
                externalFieldKey = "charc:9",
                externalFieldName = "\u0426\u0432\u0435\u0442",
                externalFieldType = "string",
                sourceType = FieldMappingSourceType.CategoryAttribute,
                sourceAttributeId = colorAttr.Id,
            },
        ]);
        await db.SaveChangesAsync();
        adapter.Add(new ExternalProduct(
            "nm-2",
            "S2",
            "Cat card",
            Brand: "Acme",
            Price: 19.5m,
            CategoryId: "42",
            CategoryName: "T-shirts",
            Attributes: new Dictionary<string, object>
            {
                ["\u0426\u0432\u0435\u0442"] = "Grey",
            }));

        var result = await svc.ApplyAsync(connection, ["nm-2"], user.Id, CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.CategoryJobs);
        var product = Assert.Single(db.products);
        Assert.Equal(category.Id, product.categoryId);
        Assert.Equal("Cat card", product.name);
        Assert.Equal("S2", product.sku);
        Assert.Equal(19.5m, product.price);
        var values = await db.productAttributeValues.ToListAsync();
        Assert.Contains(values, v => v.attributeId == brandAttr.Id && v.value == "Acme");
        Assert.Contains(values, v => v.attributeId == colorAttr.Id && v.value == "Grey");
    }

    [Fact]
    public async Task Apply_WaitsForCategoryMapping_ThenAssignsProduct()
    {
        var (svc, db, connection, user, adapter) = Create();
        adapter.Add(new ExternalProduct(
            "nm-2", "S2", "Cat card", Brand: "Acme", CategoryId: "42", CategoryName: "T-shirts"));
        var data = new TestDataBuilder(db);
        var category = data.AddCategory(connection.storeId, "T-shirts");
        var brandAttr = data.AddCategoryAttribute(category.Id, "Brand", key: "brand");
        await db.SaveChangesAsync();

        svc.CategoryWaitPoll = TimeSpan.FromMilliseconds(1);
        svc.CategoryWaitTimeout = TimeSpan.FromSeconds(2);
        var seeded = false;
        svc.OnCategoryWaitTick = async () =>
        {
            if (seeded)
                return;
            seeded = true;
            var mapping = data.AddMapping(connection.Id, category.Id, "42", "T-shirts");
            mapping.graphJson = GraphGenerator.BuildGraphJson(
            [
                new FieldSpec
                {
                    externalFieldKey = "brand",
                    externalFieldName = "Brand",
                    externalFieldType = "string",
                    sourceType = FieldMappingSourceType.CategoryAttribute,
                    sourceAttributeId = brandAttr.Id,
                },
            ]);
            await db.SaveChangesAsync();
        };

        var result = await svc.ApplyAsync(connection, ["nm-2"], user.Id, CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.CategoryJobs);
        var product = Assert.Single(db.products);
        Assert.Equal(category.Id, product.categoryId);
        Assert.Equal("Acme", Assert.Single(db.productAttributeValues).value);
    }

    [Fact]
    public async Task Apply_CategoryWaitTimeout_StillCopiesProduct()
    {
        var (svc, db, connection, user, adapter) = Create();
        svc.CategoryWaitTimeout = TimeSpan.Zero;
        adapter.Add(new ExternalProduct(
            "nm-2", "S2", "Cat card", CategoryId: "42", CategoryName: "T-shirts"));

        var result = await svc.ApplyAsync(connection, ["nm-2"], user.Id, CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.CategoryJobs);
        var job = Assert.Single(db.pendingCategoryImports);
        Assert.Equal("42", job.externalCategoryId);
        Assert.Null(Assert.Single(db.products).categoryId);
    }

    [Fact]
    public async Task Search_Url_FetchesPublicProductNotCabinetImport()
    {
        var (svc, _, connection, _, adapter) = Create();
        adapter.Add(new ExternalProduct("12345678", "SKU", "From URL", Brand: "Acme"));

        var result = await svc.SearchAsync(
            connection,
            "https://www.wildberries.ru/catalog/12345678/detail.aspx",
            50,
            null,
            CancellationToken.None);

        var hit = Assert.Single(result.Items);
        Assert.Equal("12345678", hit.ExternalId);
        Assert.Equal("From URL", hit.Name);
        Assert.Equal("Acme", hit.Brand);
        Assert.Equal(1, adapter.GetPublicCalls);
        Assert.Equal(0, adapter.SearchCalls);
        Assert.Equal(0, adapter.ImportCalls);
    }

    [Fact]
    public async Task Search_Storefront429_IsRateLimitedApiException()
    {
        var (svc, _, connection, _, adapter) = Create();
        adapter.ThrowRateLimit = true;

        var ex = await Assert.ThrowsAsync<Delegat.Infrastructure.Middleware.ApiException>(() =>
            svc.SearchAsync(connection, "nike", 50, null, CancellationToken.None));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal("rate_limited", ex.Code);
        Assert.DoesNotContain("does not indicate success", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static (
        CatalogScrapeService svc,
        DataContext db,
        MarketplaceConnection connection,
        Delegat.Models.User user,
        ScrapeAdapter adapter) Create(string? existingSku = null)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser();
        var store = data.AddStore();
        if (existingSku != null)
            data.AddProduct(store.Id, "Existing", existingSku);
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        db.SaveChanges();

        var adapter = new ScrapeAdapter();
        var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), db);
        var productSync = new ProductSyncService(
            db,
            new SingleAdapterRegistry(adapter),
            mappingService: null!,
            storageService: null!,
            NullLogger<ProductSyncService>.Instance,
            new NoopOperationLogger(),
            quota);
        var svc = new CatalogScrapeService(
            db,
            new SingleAdapterRegistry(adapter),
            productSync,
            new CategoryImportQueueService(db),
            new NoopOperationLogger(),
            quota);
        return (svc, db, connection, user, adapter);
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

    private sealed class ScrapeAdapter : IMarketplaceAdapter
    {
        private readonly Dictionary<string, ExternalProduct> _byId = new(StringComparer.Ordinal);

        public int ImportCalls { get; private set; }
        public int GetPublicCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public bool ThrowRateLimit { get; set; }

        public void Add(ExternalProduct product) => _byId[product.ExternalId] = product;

        public string Key => "fake";
        public string DisplayName => "Fake";
        public string? Description => null;
        public string? LogoUrl => null;
        public MarketplaceCapabilities Capabilities => MarketplaceCapabilities.SearchPublicCatalog;
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
            return Task.FromResult<ExternalProduct?>(null);
        }
        public Task<List<ExternalProduct>> ImportProductsBatchAsync(MarketplaceConnection connection, List<string> externalProductIds, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, string? offerIdFilter = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<PagedResult<ExternalProduct>> SearchPublicCatalogAsync(MarketplaceConnection connection, string query, int pageSize = 50, string? cursor = null, CancellationToken ct = default)
        {
            SearchCalls++;
            if (ThrowRateLimit)
                throw new HttpRequestException("429", null, System.Net.HttpStatusCode.TooManyRequests);
            return Task.FromResult(new PagedResult<ExternalProduct>([], false));
        }
        public Task<ExternalProduct?> GetPublicProductAsync(MarketplaceConnection connection, string publicId, CancellationToken ct = default)
        {
            GetPublicCalls++;
            return Task.FromResult(_byId.TryGetValue(publicId, out var p) ? p : null);
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
