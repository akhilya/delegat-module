using System.Text.Json;
using Delegat.Controllers.PublicApi;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public class PublicApiCatalogLogicTests
{
    private static (DataContext db, TestDataBuilder data, FakeCurrentUserService currentUser) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db), new FakeCurrentUserService());
    }

    private static PublicProductsController Products(DataContext db, FakeCurrentUserService currentUser) =>
        new(
            new PublicApiStoreContext(currentUser, db),
            db,
            TestDbFactory.CreateUnitOfWork(db),
            new QuotaService(TestDbFactory.CreateUnitOfWork(db)),
            new ServiceCollection().BuildServiceProvider());

    private static PublicCategoriesController Categories(DataContext db, FakeCurrentUserService currentUser) =>
        new(new PublicApiStoreContext(currentUser, db), db, TestDbFactory.CreateUnitOfWork(db), new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task StoreContext_MemberHasAccess()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        await new PublicApiStoreContext(currentUser, db).EnsureStoreAccessAsync(store.Id);
    }

    [Fact]
    public async Task StoreContext_NonMemberIsForbidden()
    {
        var (db, data, currentUser) = Create();
        var member = data.AddUser("member");
        var outsider = data.AddUser("outsider");
        var store = data.AddStore(members: member);
        await data.SaveAsync();
        currentUser.UserId = outsider.Id;
        var ex = await Assert.ThrowsAsync<ApiException>(() => new PublicApiStoreContext(currentUser, db).EnsureStoreAccessAsync(store.Id));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task ProductList_ReturnsOnlyRequestedStore()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var a = data.AddStore("A", members: user);
        var b = data.AddStore("B");
        data.AddProduct(a.Id, name: "Visible");
        data.AddProduct(b.Id, name: "Hidden");
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var result = await Products(db, currentUser).List(a.Id);
        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value);
        Assert.Contains("Visible", json);
        Assert.DoesNotContain("Hidden", json);
    }

    [Fact]
    public async Task ProductList_SearchFiltersName()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        data.AddProduct(store.Id, name: "Red sneaker");
        data.AddProduct(store.Id, name: "Blue boot");
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var result = await Products(db, currentUser).List(store.Id, search: "sneaker");
        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value);
        Assert.Contains("Red sneaker", json);
        Assert.DoesNotContain("Blue boot", json);
    }

    [Fact]
    public async Task ProductList_SearchFiltersSku()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        data.AddProduct(store.Id, sku: "ABC-001");
        data.AddProduct(store.Id, sku: "XYZ-001");
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var result = await Products(db, currentUser).List(store.Id, search: "ABC");
        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value);
        Assert.Contains("ABC-001", json);
        Assert.DoesNotContain("XYZ-001", json);
    }

    [Fact]
    public async Task ProductList_CategoryMinusOneReturnsUncategorized()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var category = data.AddCategory(store.Id);
        data.AddProduct(store.Id, name: "Loose");
        data.AddProduct(store.Id, name: "Categorized", categoryId: category.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var result = await Products(db, currentUser).List(store.Id, categoryId: -1);
        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value);
        Assert.Contains("Loose", json);
        Assert.DoesNotContain("Categorized", json);
    }

    [Fact]
    public async Task ProductList_CategoryFilterReturnsMatchingProducts()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var a = data.AddCategory(store.Id, "A");
        var b = data.AddCategory(store.Id, "B");
        data.AddProduct(store.Id, name: "A product", categoryId: a.Id);
        data.AddProduct(store.Id, name: "B product", categoryId: b.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var result = await Products(db, currentUser).List(store.Id, categoryId: a.Id);
        var json = JsonSerializer.Serialize(((OkObjectResult)result).Value);
        Assert.Contains("A product", json);
        Assert.DoesNotContain("B product", json);
    }

    [Fact]
    public async Task ProductList_ForbiddenStoreThrowsBeforeReturningItems()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore();
        data.AddProduct(store.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var ex = await Assert.ThrowsAsync<ApiException>(() => Products(db, currentUser).List(store.Id));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task ProductDelete_SetsDeletedAt()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var result = await Products(db, currentUser).Delete(store.Id, product.Id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull((await db.products.IgnoreQueryFilters().SingleAsync()).deletedAt);
    }

    [Fact]
    public async Task ProductDelete_OtherStoreProductIsNotFound()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var accessible = data.AddStore(members: user);
        var other = data.AddStore();
        var product = data.AddProduct(other.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var ex = await Assert.ThrowsAsync<ApiException>(() => Products(db, currentUser).Delete(accessible.Id, product.Id, CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task CategoryDelete_WithProductsReturnsConflict()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var category = data.AddCategory(store.Id);
        data.AddProduct(store.Id, categoryId: category.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var ex = await Assert.ThrowsAsync<ApiException>(() => Categories(db, currentUser).Delete(store.Id, category.Id, CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task ConnectionProjection_NeverContainsCredentialsJson()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, credentialsJson: "{\"token\":\"secret\"}");
        await data.SaveAsync();
        var dto = await db.marketplaceConnections.Where(c => c.storeId == store.Id).Select(c => new
        {
            id = c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt, c.createdAt
        }).SingleAsync();
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("credentialsJson", json);
        Assert.DoesNotContain("secret", json);
        Assert.Equal(connection.Id, dto.id);
    }
}
