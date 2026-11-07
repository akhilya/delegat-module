using Delegat.Infrastructure.Middleware;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class BarcodeUniquenessTests
{
    private const string Barcode = "2000000000015";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RejectsEveryProductVariantCollision(bool existingVariant, bool newVariant)
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var a = data.AddProduct(store.Id);
        var b = data.AddProduct(store.Id);
        if (existingVariant) data.AddVariant(a.Id, barcode: Barcode);
        else a.barcode = Barcode;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        if (newVariant) data.AddVariant(b.Id, barcode: Barcode);
        else (await db.products.SingleAsync(p => p.Id == b.Id)).barcode = Barcode;

        var error = await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("barcode_already_used", error.Code);
        db.ChangeTracker.Clear();
        Assert.Null((await db.products.SingleAsync(p => p.Id == b.Id)).barcode);
        Assert.False(await db.productVariants.AnyAsync(v => v.productId == b.Id));
    }

    [Fact]
    public async Task RejectsProductAndItsOwnVariantSharingBarcodeInOneSave()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var product = data.AddProduct(store.Id);
        product.barcode = Barcode;
        data.AddVariant(product.Id, barcode: Barcode);
        var error = await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
        Assert.Equal("barcode_already_used", error.Code);
        Assert.Empty(await db.products.ToListAsync());
    }

    [Fact]
    public async Task RejectsTwoNewProductsInOneSave()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        data.AddProduct(store.Id).barcode = Barcode;
        data.AddProduct(store.Id).barcode = Barcode;
        await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task TrashedProductsAndTheirVariantsStillReserveBarcodes()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var trashed = data.AddProduct(store.Id);
        trashed.deletedAt = DateTime.UtcNow;
        data.AddVariant(trashed.Id, barcode: Barcode);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        data.AddProduct(store.Id).barcode = Barcode;
        await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AllowsSameBarcodeInDifferentStoresAndUnchangedOwner()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var first = data.AddProduct(data.AddStore().Id);
        first.barcode = Barcode;
        data.AddProduct(data.AddStore().Id).barcode = Barcode;
        await db.SaveChangesAsync();
        first.name = "Renamed";
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.products.CountAsync(p => p.barcode == Barcode));
    }

    [Fact]
    public async Task AllowsClearingAndReassigningBarcodeInSameSave()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var first = data.AddProduct(store.Id);
        first.barcode = Barcode;
        var second = data.AddProduct(store.Id);
        await db.SaveChangesAsync();
        first.barcode = "";
        second.barcode = Barcode;
        await db.SaveChangesAsync();
        Assert.Null(first.barcode);
        Assert.Equal(Barcode, second.barcode);
    }

    [Fact]
    public async Task MovingProductChecksItsUnmodifiedVariantsInDestinationStore()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var firstStore = data.AddStore();
        var secondStore = data.AddStore();
        var moved = data.AddProduct(firstStore.Id);
        data.AddVariant(moved.Id, barcode: Barcode);
        data.AddProduct(secondStore.Id).barcode = Barcode;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        moved = await db.products.SingleAsync(p => p.Id == moved.Id);
        moved.storeId = secondStore.Id;
        await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SynchronousSaveAlsoRejectsDuplicates()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        data.AddProduct(store.Id).barcode = Barcode;
        await db.SaveChangesAsync();
        data.AddProduct(store.Id).barcode = Barcode;
        Assert.Throws<ApiException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task BulkEnqueueRejectsAssigningSameBarcodeToMultipleProducts()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var first = data.AddProduct(store.Id);
        var second = data.AddProduct(store.Id);
        await db.SaveChangesAsync();
        var service = new ProductBulkUpdateService(db, TestDbFactory.CreateUnitOfWork(db), new AttributeUniquenessService(db));
        var error = await Assert.ThrowsAsync<ApiException>(() => service.ValidateBeforeEnqueueAsync(
            store.Id, [first.Id, second.Id], new() { barcode = Barcode }));
        Assert.Equal("barcode_already_used", error.Code);
    }

    [Fact]
    public async Task RejectsSiblingVariantDuplicatesAfterTrimming()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        data.AddVariant(product.Id, barcode: Barcode);
        await db.SaveChangesAsync();
        data.AddVariant(product.Id, barcode: $" {Barcode} ");
        await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task UpdatingVariantCannotTakeAnExistingProductBarcode()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var product = data.AddProduct(data.AddStore().Id);
        product.barcode = Barcode;
        var variant = data.AddVariant(product.Id);
        await db.SaveChangesAsync();
        variant.barcode = Barcode;
        await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MovingVariantToAnotherStoreChecksDestinationNamespace()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var first = data.AddProduct(data.AddStore().Id);
        var second = data.AddProduct(data.AddStore().Id);
        second.barcode = Barcode;
        var variant = data.AddVariant(first.Id, barcode: Barcode);
        await db.SaveChangesAsync();
        variant.productId = second.Id;
        await Assert.ThrowsAsync<ApiException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task BulkGenerationStaysUniqueAcrossSaveChunksAndPreservesExistingCodes()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var existing = data.AddProduct(store.Id);
        existing.barcode = Barcode;
        data.AddVariant(existing.Id, barcode: "2000000000022");
        var products = Enumerable.Range(0, 120).Select(_ => data.AddProduct(store.Id)).ToList();
        products.Add(existing);
        await db.SaveChangesAsync();
        var service = new ProductBulkUpdateService(db, TestDbFactory.CreateUnitOfWork(db), new AttributeUniquenessService(db));

        var result = await service.ExecuteAsync(store.Id, products.Select(p => p.Id).ToList(), new() { generateBarcodes = true });

        Assert.Equal(121, result.updatedCount);
        Assert.Equal(0, result.failedCount);
        Assert.Equal(Barcode, existing.barcode);
        var codes = await db.products.Select(p => p.barcode).ToListAsync();
        Assert.All(codes, code => Assert.True(Controllers.ProductController.IsValidEanBarcode(code!)));
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.DoesNotContain("2000000000022", codes);
    }

    [Fact]
    public async Task BulkGenerationRetriesPersistedAndInBatchCollisions()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var existing = data.AddProduct(store.Id);
        existing.barcode = Barcode;
        data.AddVariant(existing.Id, barcode: "2000000000022");
        var first = data.AddProduct(store.Id);
        var second = data.AddProduct(store.Id);
        await db.SaveChangesAsync();
        var candidates = new Queue<string>([Barcode, "2000000000022", "2000000000039", "2000000000039", "2000000000046"]);
        var service = new ProductBulkUpdateService(db, TestDbFactory.CreateUnitOfWork(db),
            new AttributeUniquenessService(db), () => candidates.Dequeue());

        var result = await service.ExecuteAsync(store.Id, [first.Id, second.Id], new() { generateBarcodes = true });

        Assert.Equal(2, result.updatedCount);
        Assert.Equal(0, result.failedCount);
        Assert.Equal("2000000000039", first.barcode);
        Assert.Equal("2000000000046", second.barcode);
        Assert.Empty(candidates);
    }
}
