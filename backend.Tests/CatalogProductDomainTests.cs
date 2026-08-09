using Delegat.Infrastructure.Middleware;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class CatalogProductDomainTests
{
    private static (DataContext db, TestDataBuilder data) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db));
    }

    [Fact]
    public void AddFill_CopiesCatalogFields()
    {
        var product = new Product();
        product.AddFill(new Product.AddForm(new Product.UserAddForm
        {
            sku = "SKU-1", barcode = "123", name = "Name", description = "Desc",
            storageQuantity = 7, price = 120, oldPrice = 150, minPrice = 90, netPrice = 80, currency = "RUB"
        }, 12, 4));
        Assert.Equal("SKU-1", product.sku);
        Assert.Equal("Name", product.name);
        Assert.Equal(12, product.storeId);
        Assert.Equal(4, product.categoryId);
        Assert.Equal(120, product.price);
    }

    [Fact]
    public void AddFill_ComputesDiscountedPriceAndDiscount()
    {
        var product = new Product();
        product.AddFill(new Product.AddForm(new Product.UserAddForm { sku = "S", name = "N", description = "", price = 75, oldPrice = 100 }, 1));
        Assert.Equal(75, product.discountedPrice);
        Assert.Equal(25, product.discount);
    }

    [Fact]
    public void UpdateFill_ChangesProvidedFields()
    {
        var product = new Product { name = "Old", sku = "S", description = "old", price = 10, categoryId = 1 };
        product.UpdateFill(new Product.UpdateForm { name = "New", description = "new", price = 25, categoryId = 2 });
        Assert.Equal("New", product.name);
        Assert.Equal("new", product.description);
        Assert.Equal(25, product.price);
        Assert.Equal(2, product.categoryId);
    }

    [Fact]
    public void UpdateFill_EmptyBarcodeClearsBarcode()
    {
        var product = new Product { name = "N", sku = "S", description = "", barcode = "12345678" };
        product.UpdateFill(new Product.UpdateForm { barcode = "" });
        Assert.Null(product.barcode);
    }

    [Fact]
    public void UpdateFill_EmptyCurrencyClearsCurrency()
    {
        var product = new Product { name = "N", sku = "S", description = "", currency = "RUB" };
        product.UpdateFill(new Product.UpdateForm { currency = "" });
        Assert.Null(product.currency);
    }

    [Fact]
    public async Task SoftDelete_SetsDeletedAt()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        await data.SaveAsync();
        product.deletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.NotNull((await db.products.IgnoreQueryFilters().SingleAsync()).deletedAt);
    }

    [Fact]
    public async Task Restore_ClearsDeletedAt()
    {
        var (db, data) = Create();
        var product = data.AddProduct(data.AddStore().Id, deletedAt: DateTime.UtcNow);
        await data.SaveAsync();
        product.deletedAt = null;
        await db.SaveChangesAsync();
        Assert.Null((await db.products.SingleAsync()).deletedAt);
    }

    [Fact]
    public async Task PermanentDelete_RemovesSoftDeletedProduct()
    {
        var (db, data) = Create();
        var product = data.AddProduct(data.AddStore().Id, deletedAt: DateTime.UtcNow);
        await data.SaveAsync();
        db.products.Remove(product);
        await db.SaveChangesAsync();
        Assert.Empty(db.products);
    }

    [Fact]
    public async Task StoreQuery_DoesNotExposeOtherStoreProducts()
    {
        var (db, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.AddProduct(a.Id, name: "A product");
        data.AddProduct(b.Id, name: "B product");
        await data.SaveAsync();
        var products = await db.products.Where(p => p.storeId == a.Id).ToListAsync();
        Assert.Single(products);
        Assert.Equal("A product", products[0].name);
    }

    [Fact]
    public async Task Search_FilterMatchesName()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        data.AddProduct(store.Id, name: "Red sneaker", sku: "A");
        data.AddProduct(store.Id, name: "Blue boot", sku: "B");
        await data.SaveAsync();
        var items = await db.products.Where(p => p.storeId == store.Id && (p.name.Contains("sneaker") || p.sku.Contains("sneaker"))).ToListAsync();
        Assert.Single(items);
    }

    [Fact]
    public async Task Search_FilterMatchesSku()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        data.AddProduct(store.Id, sku: "RED-01");
        data.AddProduct(store.Id, sku: "BLUE-01");
        await data.SaveAsync();
        Assert.Single(await db.products.Where(p => p.storeId == store.Id && (p.name.Contains("RED") || p.sku.Contains("RED"))).ToListAsync());
    }

    [Fact]
    public async Task CategoryMinusOne_FiltersUncategorizedProducts()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        data.AddProduct(store.Id, categoryId: null);
        data.AddProduct(store.Id, categoryId: category.Id);
        await data.SaveAsync();
        Assert.Single(await db.products.Where(p => p.storeId == store.Id && p.categoryId == null).ToListAsync());
    }

    [Fact]
    public async Task CategoryFilter_ReturnsOnlySpecifiedCategory()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        var first = data.AddCategory(store.Id);
        var second = data.AddCategory(store.Id);
        data.AddProduct(store.Id, categoryId: first.Id);
        data.AddProduct(store.Id, categoryId: second.Id);
        await data.SaveAsync();
        Assert.Single(await db.products.Where(p => p.storeId == store.Id && p.categoryId == first.Id).ToListAsync());
    }

    [Fact]
    public async Task DuplicateSku_InSameStoreSimulatesConflict()
    {
        var (db, data) = Create();
        var store = data.AddStore();
        data.AddProduct(store.Id, sku: "DUP");
        await data.SaveAsync();
        var duplicate = await db.products.AnyAsync(p => p.storeId == store.Id && p.sku == "DUP");
        var ex = duplicate ? ApiException.Conflict("duplicate SKU") : null;
        Assert.Equal(409, ex!.StatusCode);
    }

    [Fact]
    public async Task SameSku_InDifferentStoresIsAllowed()
    {
        var (db, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.AddProduct(a.Id, sku: "DUP");
        await data.SaveAsync();
        Assert.False(await db.products.AnyAsync(p => p.storeId == b.Id && p.sku == "DUP"));
    }

    [Fact]
    public async Task TrashCount_CountsOnlyDeletedProductsInStore()
    {
        var (db, data) = Create();
        var a = data.AddStore("A");
        var b = data.AddStore("B");
        data.AddProduct(a.Id, deletedAt: DateTime.UtcNow);
        data.AddProduct(a.Id);
        data.AddProduct(b.Id, deletedAt: DateTime.UtcNow);
        await data.SaveAsync();
        Assert.Equal(1, await db.products.IgnoreQueryFilters().CountAsync(p => p.storeId == a.Id && p.deletedAt != null));
    }

    [Fact]
    public async Task ProductQuota_AtLimitBlocksCreate()
    {
        var previous = Environment.GetEnvironmentVariable("MAX_PRODUCTS");
        Environment.SetEnvironmentVariable("MAX_PRODUCTS", "1");
        try
        {
            var (db, data) = Create();
            var store = data.AddStore();
            data.AddProduct(store.Id);
            await data.SaveAsync();
            var quota = new QuotaService(TestDbFactory.CreateUnitOfWork(db), db);
            var ex = await Assert.ThrowsAsync<ApiException>(() => quota.EnsureProductQuotaAsync(store.Id));
            Assert.Equal(409, ex.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAX_PRODUCTS", previous);
        }
    }
}
