using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

[Collection("EnvVars")]
public class MarketplaceConnectionCreateTests : IDisposable
{
    private readonly string? _previousMaxConnections;

    public MarketplaceConnectionCreateTests()
    {
        _previousMaxConnections = Environment.GetEnvironmentVariable("MAX_CONNECTIONS");
        // Dev shells often load MAX_CONNECTIONS=1 from .env.dev; uniqueness tests need room for two rows.
        Environment.SetEnvironmentVariable("MAX_CONNECTIONS", "10");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MAX_CONNECTIONS", _previousMaxConnections);
    }

    [Fact]
    public async Task Create_DoesNotPersist_WhenAdapterTestFails()
    {
        var (svc, db, data) = CreateService(testSucceeds: false, testMessage: "Invalid API key");
        var store = data.AddStore();
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon",
                adapterKey = "ozon",
                credentialsJson = """{"apiKey":"bad"}""",
                isActive = true,
            },
            store.Id));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("connection_test_failed", ex.Code);
        Assert.Contains("Invalid API key", ex.Message);
        Assert.Equal(0, await db.marketplaceConnections.CountAsync());
    }

    [Fact]
    public async Task Create_DoesNotPersist_WhenAdapterTestThrows()
    {
        var (svc, db, data) = CreateService(throwOnTest: true);
        var store = data.AddStore();
        await data.SaveAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon",
                adapterKey = "ozon",
                credentialsJson = """{"apiKey":"x"}""",
                isActive = true,
            },
            store.Id));

        Assert.Equal("connection_test_failed", ex.Code);
        Assert.Equal(0, await db.marketplaceConnections.CountAsync());
    }

    [Fact]
    public async Task Create_Persists_WhenAdapterTestSucceeds()
    {
        var (svc, db, data) = CreateService(testSucceeds: true, testMessage: "ok");
        var store = data.AddStore();
        await data.SaveAsync();

        var created = await svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon main",
                adapterKey = "ozon",
                credentialsJson = """{"apiKey":"good"}""",
                isActive = true,
            },
            store.Id);

        Assert.True(created.Id > 0);
        var saved = await db.marketplaceConnections.SingleAsync();
        Assert.Equal(store.Id, saved.storeId);
        Assert.Equal("Ozon main", saved.name);
        Assert.Equal("ozon", saved.adapterKey);
        Assert.False(string.IsNullOrEmpty(saved.credentialsJson));
        Assert.NotEqual("""{"apiKey":"good"}""", saved.credentialsJson);
    }

    [Fact]
    public async Task Create_AllowsSecondConnectionForSameAdapter_WhenRemoteShopDiffers()
    {
        var (svc, db, data) = CreateService(testSucceeds: true, testMessage: "ok");
        var store = data.AddStore();
        await data.SaveAsync();

        await svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon A",
                adapterKey = "ozon",
                credentialsJson = """{"clientId":"111","apiKey":"one"}""",
                isActive = true,
            },
            store.Id);

        var created = await svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon B",
                adapterKey = "ozon",
                credentialsJson = """{"clientId":"222","apiKey":"two"}""",
                isActive = true,
            },
            store.Id);

        Assert.True(created.Id > 0);
        Assert.Equal(2, await db.marketplaceConnections.CountAsync());
    }

    [Fact]
    public async Task Create_RejectsSameRemoteShop_EvenWithDifferentApiKey()
    {
        var (svc, db, data) = CreateService(testSucceeds: true, testMessage: "ok");
        var store = data.AddStore();
        await data.SaveAsync();

        await svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon A",
                adapterKey = "ozon",
                credentialsJson = """{"clientId":"111","apiKey":"one"}""",
                isActive = true,
            },
            store.Id);

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon B",
                adapterKey = "ozon",
                credentialsJson = """{"clientId":"111","apiKey":"two"}""",
                isActive = true,
            },
            store.Id));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("connection_remote_exists", ex.Code);
        Assert.Equal(1, await db.marketplaceConnections.CountAsync());
    }

    [Fact]
    public async Task Create_AllowsSameAdapterInAnotherStore()
    {
        var (svc, db, data) = CreateService(testSucceeds: true, testMessage: "ok");
        var first = data.AddStore();
        var second = data.AddStore();
        await data.SaveAsync();

        await svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon 1",
                adapterKey = "ozon",
                credentialsJson = """{"apiKey":"one"}""",
                isActive = true,
            },
            first.Id);

        var created = await svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Ozon 2",
                adapterKey = "ozon",
                credentialsJson = """{"apiKey":"two"}""",
                isActive = true,
            },
            second.Id);

        Assert.True(created.Id > 0);
        Assert.Equal(2, await db.marketplaceConnections.CountAsync());
    }

    [Fact]
    public async Task Create_UnknownAdapter_DoesNotPersist()
    {
        var (svc, db, data) = CreateService(testSucceeds: true);
        var store = data.AddStore();
        await data.SaveAsync();

        await Assert.ThrowsAsync<ApiException>(() => svc.CreateAsync(
            new MarketplaceConnection.UserAddForm
            {
                name = "Nope",
                adapterKey = "missing",
                credentialsJson = "{}",
                isActive = true,
            },
            store.Id));

        Assert.Equal(0, await db.marketplaceConnections.CountAsync());
    }

    private static (
        MarketplaceConnectionCreateService svc,
        DataContext db,
        TestDataBuilder data) CreateService(
        bool testSucceeds = true,
        string? testMessage = null,
        bool throwOnTest = false)
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var quota = new QuotaService(uow, db);
        var encryption = new EncryptionService(Convert.ToBase64String(new byte[32]));
        var adapter = new FakeTestAdapter
        {
            Succeeds = testSucceeds,
            Message = testMessage,
            ThrowOnTest = throwOnTest,
        };
        var svc = new MarketplaceConnectionCreateService(
            new FakeAdapterRegistry(adapter),
            uow,
            quota,
            encryption);
        return (svc, db, data);
    }

    private sealed class FakeAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public IMarketplaceAdapter? GetByKey(string key) =>
            key == adapter.Key ? adapter : null;
        public bool Exists(string key) => key == adapter.Key;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private sealed class FakeTestAdapter : IMarketplaceAdapter
    {
        public bool Succeeds { get; set; } = true;
        public string? Message { get; set; }
        public bool ThrowOnTest { get; set; }

        public string Key => "ozon";
        public string DisplayName => "Ozon";
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

        public Task<ConnectionTestResult> TestConnectionAsync(
            MarketplaceConnection connection,
            CancellationToken ct = default)
        {
            if (ThrowOnTest)
                throw new InvalidOperationException("marketplace unreachable");
            return Task.FromResult(new ConnectionTestResult(Succeeds, Message));
        }

        public Task<List<ExternalCategory>> GetCategoriesAsync(MarketplaceConnection connection, string? parentId = null, string? searchQuery = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<ExternalField>> GetCategoryFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<List<ExternalField>> GetPriceFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
            => Task.FromResult(new List<ExternalField>());
        public Task<CreateCategoryResult> CreateCategoryAsync(MarketplaceConnection connection, CreateCategoryRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ExternalProduct?> ImportProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
            => throw new NotImplementedException();
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
