using Delegat;
using Delegat.Controllers.PublicApi;
using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public class PublicApiProductWriteTests
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
            new QuotaService(TestDbFactory.CreateUnitOfWork(db), db),
            new AttributeUniquenessService(db));

    private static PublicCategoriesController Categories(DataContext db, FakeCurrentUserService currentUser) =>
        new(new PublicApiStoreContext(currentUser, db), db, TestDbFactory.CreateUnitOfWork(db), new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task Create_RejectsCategoryFromAnotherStore()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var other = data.AddStore();
        var foreign = data.AddCategory(other.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() => Products(db, currentUser).Create(
            store.Id,
            new Product.UserAddForm
            {
                sku = "SKU-X",
                name = "X",
                description = "",
                categoryId = foreign.Id
            },
            CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("category_not_found", ex.Code);
    }

    [Fact]
    public async Task Create_RejectsInvalidBarcode()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() => Products(db, currentUser).Create(
            store.Id,
            new Product.UserAddForm
            {
                sku = "SKU-X",
                name = "X",
                description = "",
                barcode = "123"
            },
            CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("invalid_barcode", ex.Code);
    }

    [Fact]
    public async Task Update_ReplacesAttributeValues()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var category = data.AddCategory(store.Id);
        var attr = data.AddCategoryAttribute(category.Id, "Color");
        var storeAttr = data.AddStoreAttribute(store.Id, "Brand");
        var product = data.AddProduct(store.Id, categoryId: category.Id);
        await data.SaveAsync();
        db.ChangeTracker.Clear();
        currentUser.UserId = user.Id;

        var result = await Products(db, currentUser).Update(
            store.Id,
            product.Id,
            new Product.UpdateForm
            {
                name = "Updated",
                attributeValues = new List<ProductAttributeValue.UserValueForm>
                {
                    new() { attributeId = attr.Id, value = "Red" }
                },
                storeAttributeValues = new List<StoreProductAttributeValue.UserValueForm>
                {
                    new() { storeAttributeId = storeAttr.Id, value = "Nike" }
                }
            },
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        var json = PublicApiTestJson.FromOk(await Products(db, currentUser).Get(store.Id, product.Id, CancellationToken.None));
        Assert.Equal("Updated", json.GetProperty("name").GetString());
        Assert.Equal("Red", json.GetProperty("attributeValues")[0].GetProperty("value").GetString());
        Assert.Equal("Nike", json.GetProperty("storeAttributeValues")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Variants_CrudRoundTrip()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();
        currentUser.UserId = user.Id;
        var products = Products(db, currentUser);

        var created = Assert.IsType<ObjectResult>(await products.CreateVariant(
            store.Id,
            product.Id,
            new PublicProductsController.VariantWriteRequest { size = "M", sku = "P-M", price = 10m, quantity = 3 },
            CancellationToken.None));
        Assert.Equal(201, created.StatusCode);
        var id = PublicApiTestJson.FromObject(created.Value).GetProperty("id").GetInt32();

        var listed = PublicApiTestJson.FromOk(await products.ListVariants(store.Id, product.Id, CancellationToken.None));
        Assert.Equal("M", Assert.Single(listed.EnumerateArray()).GetProperty("size").GetString());

        await products.UpdateVariant(
            store.Id,
            product.Id,
            id,
            new PublicProductsController.VariantWriteRequest { size = "L", quantity = 8 },
            CancellationToken.None);

        var afterPatch = PublicApiTestJson.FromOk(await products.Get(store.Id, product.Id, CancellationToken.None));
        var variant = Assert.Single(afterPatch.GetProperty("variants").EnumerateArray());
        Assert.Equal("L", variant.GetProperty("size").GetString());
        Assert.Equal(8, variant.GetProperty("quantity").GetInt32());
        Assert.Equal("P-M", variant.GetProperty("sku").GetString());

        await products.DeleteVariant(store.Id, product.Id, id, CancellationToken.None);
        Assert.Equal(0, PublicApiTestJson.FromOk(await products.ListVariants(store.Id, product.Id, CancellationToken.None)).GetArrayLength());
    }

    [Fact]
    public async Task Variant_OnMissingProduct_IsNotFound()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() => Products(db, currentUser).CreateVariant(
            store.Id,
            999,
            new PublicProductsController.VariantWriteRequest { size = "M" },
            CancellationToken.None));
        Assert.Equal("product_not_found", ex.Code);
    }

    [Fact]
    public async Task CategoryCreate_PersistsHasSizes()
    {
        var (db, data, currentUser) = Create();
        var user = data.AddUser();
        var store = data.AddStore(members: user);
        await data.SaveAsync();
        currentUser.UserId = user.Id;

        var created = Assert.IsType<ObjectResult>(await Categories(db, currentUser).Create(
            store.Id,
            new Category.UserAddForm { name = "Apparel", hasSizes = true },
            CancellationToken.None));
        Assert.Equal(201, created.StatusCode);
        var id = PublicApiTestJson.FromObject(created.Value).GetProperty("id").GetInt32();

        var json = PublicApiTestJson.FromOk(await Categories(db, currentUser).Get(store.Id, id, CancellationToken.None));
        Assert.True(json.GetProperty("hasSizes").GetBoolean());
    }
}
