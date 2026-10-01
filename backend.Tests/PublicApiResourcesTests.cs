using Delegat;
using Delegat.Controllers.PublicApi;
using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class PublicApiResourcesTests
{
    private static (DataContext db, TestDataBuilder data, FakeCurrentUserService currentUser) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db), new FakeCurrentUserService());
    }

    private static PublicGroupsController Groups(DataContext db, FakeCurrentUserService currentUser) =>
        new(new PublicApiStoreContext(currentUser, db), db, TestDbFactory.CreateUnitOfWork(db), new ServiceCollection().BuildServiceProvider());

    private static PublicStoreAttributesController StoreAttributes(DataContext db, FakeCurrentUserService currentUser) =>
        new(new PublicApiStoreContext(currentUser, db), db, TestDbFactory.CreateUnitOfWork(db), new ServiceCollection().BuildServiceProvider());

    private static PublicMetaController Meta(DataContext db, FakeCurrentUserService currentUser) =>
        new(new PublicApiStoreContext(currentUser, db), db, currentUser);

    private static PublicConnectionsController Connections(DataContext db, FakeCurrentUserService currentUser) =>
        new(new PublicApiStoreContext(currentUser, db), db, new EmptyAdapters());

    private static PublicSyncController Sync(DataContext db, FakeCurrentUserService currentUser) =>
        new(
            new PublicApiStoreContext(currentUser, db),
            db,
            new NoopOperationLogger(),
            new UnusedScopeFactory(),
            NullLogger<PublicSyncController>.Instance,
            new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));

    [Fact]
    public async Task Me_ReturnsKeyOwner()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser("partner");
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        currentUser.User = user;

        var json = PublicApiTestJson.FromOk(await Meta(db, currentUser).Me(CancellationToken.None));
        Assert.Equal(user.Id, json.GetProperty("id").GetInt32());
        Assert.Equal("partner", json.GetProperty("userName").GetString());
        Assert.Equal("partner@example.com", json.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Stores_ReturnsOnlyMemberships()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var mine = data.AddStore("Mine", members: user);
        data.AddStore("Other");
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var json = PublicApiTestJson.FromOk(await Meta(db, currentUser).ListStores(CancellationToken.None));
        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.Equal(mine.Id, item.GetProperty("id").GetInt32());
        Assert.Equal("Mine", item.GetProperty("name").GetString());
    }

    [Fact]
    public async Task StoreAttributeCreate_PersistsOptions()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var created = Assert.IsType<ObjectResult>(await StoreAttributes(db, currentUser).Create(
            store.Id,
            new StoreAttribute.UserAddForm
            {
                name = "Brand",
                key = "brand",
                type = AttributeType.Select,
                options = "[\"A\",\"B\"]",
                defaultValue = "A",
                description = "Maker"
            },
            CancellationToken.None));
        Assert.Equal(201, created.StatusCode);
        var id = PublicApiTestJson.FromObject(created.Value).GetProperty("id").GetInt32();

        var json = PublicApiTestJson.FromOk(await StoreAttributes(db, currentUser).Get(store.Id, id, CancellationToken.None));
        Assert.Equal("[\"A\",\"B\"]", json.GetProperty("options").GetString());
        Assert.Equal("A", json.GetProperty("defaultValue").GetString());
        Assert.Equal("Maker", json.GetProperty("description").GetString());
    }

    [Fact]
    public async Task StoreAttributeCreate_PersistsValueConstraints()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var created = Assert.IsType<ObjectResult>(await StoreAttributes(db, currentUser).Create(
            store.Id,
            new StoreAttribute.UserAddForm
            {
                name = "Color",
                key = "color",
                type = AttributeType.Select,
                options = "[\"Red\"]",
                allowCustomValues = true,
                maxValues = 2,
                maxLength = 30,
                validationPattern = "^[A-Z]"
            },
            CancellationToken.None));
        Assert.Equal(201, created.StatusCode);
        var id = PublicApiTestJson.FromObject(created.Value).GetProperty("id").GetInt32();

        var json = PublicApiTestJson.FromOk(await StoreAttributes(db, currentUser).Get(store.Id, id, CancellationToken.None));
        Assert.True(json.GetProperty("allowCustomValues").GetBoolean());
        Assert.Equal(2, json.GetProperty("maxValues").GetInt32());
        Assert.Equal(30, json.GetProperty("maxLength").GetInt32());
        Assert.Equal("^[A-Z]", json.GetProperty("validationPattern").GetString());
    }

    [Fact]
    public async Task StoreAttributeCreate_DuplicateKeyConflicts()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        data.AddStoreAttribute(store.Id, key: "brand");
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() => StoreAttributes(db, currentUser).Create(
            store.Id,
            new StoreAttribute.UserAddForm { name = "Brand", key = "brand", type = AttributeType.Text },
            CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("store_attribute_key_used", ex.Code);
    }

    [Fact]
    public async Task Groups_AddMovesProductBetweenGroups()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        var from = data.AddGroup(store.Id, "From", product);
        var to = data.AddGroup(store.Id, "To");
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        await Groups(db, currentUser).AddProducts(
            store.Id,
            to.Id,
            new PublicGroupsController.AddProductsRequest { productIds = new List<int> { product.Id } },
            CancellationToken.None);

        var fromJson = PublicApiTestJson.FromOk(await Groups(db, currentUser).Get(store.Id, from.Id, CancellationToken.None));
        var toJson = PublicApiTestJson.FromOk(await Groups(db, currentUser).Get(store.Id, to.Id, CancellationToken.None));
        Assert.Equal(0, fromJson.GetProperty("productIds").GetArrayLength());
        Assert.Equal(product.Id, Assert.Single(toJson.GetProperty("productIds").EnumerateArray()).GetInt32());
    }

    [Fact]
    public async Task Groups_RemoveMissingMember_IsNotFound()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var group = data.AddGroup(store.Id);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            Groups(db, currentUser).RemoveProduct(store.Id, group.Id, product.Id, CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("group_member_not_found", ex.Code);
    }

    [Fact]
    public async Task Connections_OmitCredentials()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var connection = data.AddConnection(store.Id, credentialsJson: "{\"token\":\"secret-token\"}");
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var list = PublicApiTestJson.FromOk(await Connections(db, currentUser).List(store.Id, CancellationToken.None));
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(connection.Id, item.GetProperty("id").GetInt32());
        Assert.False(item.TryGetProperty("credentialsJson", out _));
        Assert.DoesNotContain("secret-token", item.ToString());

        var detail = PublicApiTestJson.FromOk(await Connections(db, currentUser).Get(store.Id, connection.Id, CancellationToken.None));
        Assert.DoesNotContain("secret-token", detail.ToString());
    }

    [Fact]
    public async Task OperationGet_Missing_IsNotFound()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            Connections(db, currentUser).GetOperation(store.Id, "missing", CancellationToken.None));
        Assert.Equal("operation_not_found", ex.Code);
    }

    [Fact]
    public async Task OperationGet_ReturnsLogs()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var op = data.AddOperation(store.Id, user.Id, status: "completed", operationId: "op-abc");
        db.operationLogEntries.Add(new OperationLogEntry
        {
            operationId = op.operationId,
            level = "info",
            message = "done",
            timestamp = DateTime.UtcNow
        });
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var json = PublicApiTestJson.FromOk(await Connections(db, currentUser).GetOperation(store.Id, "op-abc", CancellationToken.None));
        Assert.Equal("completed", json.GetProperty("status").GetString());
        Assert.Equal("done", json.GetProperty("logs")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Sync_InactiveConnection_IsRejected()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var connection = data.AddConnection(store.Id, isActive: false);
        data.AddProduct(store.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() => Sync(db, currentUser).ExportProducts(
            store.Id,
            connection.Id,
            new PublicSyncController.ProductIdsRequest(),
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("connection_inactive", ex.Code);
    }

    [Fact]
    public async Task ImportProducts_UnmappedWithoutForce_QueuesNothing()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var connection = data.AddConnection(store.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() => Sync(db, currentUser).ImportProducts(
            store.Id,
            connection.Id,
            new PublicSyncController.ImportProductsRequest
            {
                products = new List<PublicSyncController.ImportProductItem>
                {
                    new() { externalId = "ext-1", externalCategoryId = "unknown" }
                }
            },
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("import_nothing_queued", ex.Code);
    }

    private sealed class UnusedScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("scope not used in this test");
    }

    private sealed class EmptyAdapters : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => Array.Empty<IMarketplaceAdapter>();
        public IMarketplaceAdapter? GetByKey(string key) => null;
        public bool Exists(string key) => false;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => Array.Empty<MarketplaceInfo>();
    }

    private sealed class NoopOperationLogger : IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null) =>
            Task.CompletedTask;

        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }
}
