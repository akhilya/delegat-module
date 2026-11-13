using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class ProductBulkUpdateTransactionTests
{
    [Fact]
    public async Task FailedMiddleChunk_RollsBackVariantsAndAttributesAndDoesNotLeakIntoNextSave()
    {
        var fault = new CatalogWriteFault();
        await using var db = await CreateDbAsync(fault);
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id);
        var attribute = data.AddCategoryAttribute(category.Id, type: AttributeType.Text);
        var products = Enumerable.Range(0, 105).Select(_ => data.AddProduct(store.Id, categoryId: category.Id, price: 100m)).ToList();
        var variant = data.AddVariant(products[50].Id, price: 200m);
        db.productAttributeValues.Add(new ProductAttributeValue { productId = products[50].Id, attributeId = attribute.Id, value = "old" });
        var job = new PendingProductBulkUpdate { storeId = store.Id, batchId = "bulk-failure", status = ProductBulkUpdateStatus.InProgress };
        db.pendingProductBulkUpdates.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        job = await db.pendingProductBulkUpdates.SingleAsync();
        fault.ShouldFail = command => command.CommandText.Contains("UPDATE \"products\"")
            && command.Parameters.Cast<System.Data.Common.DbParameter>().Any(p => Equals(p.Value, products[50].Id));
        var maxTracked = 0;
        db.SavingChanges += (_, _) => maxTracked = Math.Max(maxTracked, db.ChangeTracker.Entries<Product>().Count());
        var logger = new RecordingCatalogLogger(db)
        {
            OnProgress = async (completed, failed) =>
            {
                Assert.Equal(completed, await db.products.AsNoTracking().CountAsync(p => p.price == 110m));
                Assert.Equal(completed, job.completedCount);
                Assert.Equal(failed, job.failedCount);
            }
        };

        var result = await Service(db).ExecuteAsync(store.Id, products.Select(p => p.Id).ToList(), new ProductBulkUpdateFields
        {
            priceDelta = 10m,
            attributeValues = [new() { attributeId = attribute.Id, value = "new" }]
        }, operationLogger: logger, operationId: job.batchId, job: job);

        Assert.Equal(55, result.updatedCount);
        Assert.Equal(50, result.failedCount);
        Assert.Equal(105, job.processedCount);
        Assert.InRange(maxTracked, 1, 50);
        Assert.True(fault.Failures > 0);
        Assert.Empty(db.ChangeTracker.Entries<Product>());
        fault.ShouldFail = null;
        await db.SaveChangesAsync(); // The status save must not persist the failed batch.
        db.ChangeTracker.Clear();
        Assert.Equal(100m, (await db.products.SingleAsync(p => p.Id == products[50].Id)).price);
        Assert.Equal(200m, (await db.productVariants.SingleAsync(v => v.Id == variant.Id)).price);
        Assert.Equal("old", (await db.productAttributeValues.SingleAsync(v => v.productId == products[50].Id)).value);
        Assert.Equal(56, await db.productAttributeValues.CountAsync());
        Assert.Equal(55, (await db.pendingProductBulkUpdates.SingleAsync()).completedCount);
    }

    [Fact]
    public async Task CancellationAndResume_AppliesPriceDeltaOnlyOnceToCommittedPrefix()
    {
        var fault = new CatalogWriteFault();
        await using var db = await CreateDbAsync(fault);
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var ids = Enumerable.Range(0, 105).Select(_ => data.AddProduct(store.Id, price: 100m).Id).ToList();
        var job = new PendingProductBulkUpdate { storeId = store.Id, batchId = "resume", status = ProductBulkUpdateStatus.InProgress };
        db.pendingProductBulkUpdates.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        job = await db.pendingProductBulkUpdates.SingleAsync();
        using var cancellation = new CancellationTokenSource();
        fault.ShouldFail = command => command.CommandText.Contains("UPDATE \"products\"")
            && command.Parameters.Cast<System.Data.Common.DbParameter>().Any(p => Equals(p.Value, ids[50]));
        fault.BeforeFailure = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db).ExecuteAsync(
            store.Id, ids, new ProductBulkUpdateFields { priceDelta = 10m }, cancellation.Token, job: job));

        Assert.Equal(50, job.completedCount);
        Assert.Equal(50, job.processedCount);
        fault.ShouldFail = null;
        // Persisting cancellation must not flush the second batch's staged mutations.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        job = await db.pendingProductBulkUpdates.SingleAsync();
        var result = await Service(db).ExecuteAsync(store.Id, ids, new ProductBulkUpdateFields { priceDelta = 10m }, job: job);
        Assert.Equal(105, result.updatedCount);
        Assert.Equal(0, result.failedCount);
        Assert.Equal(105, job.processedCount);
        Assert.All(await db.products.AsNoTracking().ToListAsync(), p => Assert.Equal(110m, p.price));
    }

    [Fact]
    public async Task DuplicateAndForeignIds_AreCountedOnceAndCannotMutateAnotherStore()
    {
        await using var db = await CreateDbAsync(new CatalogWriteFault());
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var own = data.AddProduct(store.Id, price: 100m);
        var foreign = data.AddProduct(data.AddStore().Id, price: 100m);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = await Service(db).ExecuteAsync(store.Id, [own.Id, own.Id, foreign.Id, 9999], new ProductBulkUpdateFields { priceDelta = 10m });
        Assert.Equal(1, result.updatedCount);
        Assert.Equal(2, result.failedCount);
        Assert.Equal(110m, (await db.products.SingleAsync(p => p.Id == own.Id)).price);
        Assert.Equal(100m, (await db.products.SingleAsync(p => p.Id == foreign.Id)).price);
    }

    private static ProductBulkUpdateService Service(DataContext db) => new(db, TestDbFactory.CreateUnitOfWork(db), new AttributeUniquenessService(db));

    [Fact]
    public async Task LegacyJobWithoutCheckpoint_IsNotReplayed()
    {
        await using var db = await CreateDbAsync(new CatalogWriteFault());
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var product = data.AddProduct(store.Id, price: 100m);
        var job = new PendingProductBulkUpdate { storeId = store.Id, batchId = "legacy", processedCount = -1 };
        db.pendingProductBulkUpdates.Add(job);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        job = await db.pendingProductBulkUpdates.SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).ExecuteAsync(store.Id,
            [product.Id], new ProductBulkUpdateFields { priceDelta = 10m }, job: job));
        Assert.Equal(100m, (await db.products.AsNoTracking().SingleAsync()).price);
    }

    [Fact]
    public async Task StaleConcurrentCheckpoint_CannotApplyDeltaTwice()
    {
        await using var db = await CreateDbAsync(new CatalogWriteFault());
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var product = data.AddProduct(store.Id, price: 100m);
        db.pendingProductBulkUpdates.Add(new PendingProductBulkUpdate { storeId = store.Id, batchId = "concurrent" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await using var staleDb = new DataContext(new DbContextOptionsBuilder<DataContext>()
            .UseSqlite(db.Database.GetDbConnection()).Options);
        var staleJob = await staleDb.pendingProductBulkUpdates.SingleAsync();
        var job = await db.pendingProductBulkUpdates.SingleAsync();
        await Service(db).ExecuteAsync(store.Id, [product.Id], new ProductBulkUpdateFields { priceDelta = 10m }, job: job);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Service(staleDb).ExecuteAsync(store.Id,
            [product.Id], new ProductBulkUpdateFields { priceDelta = 10m }, job: staleJob));

        Assert.Equal(110m, (await db.products.AsNoTracking().SingleAsync()).price);
        Assert.Equal(1, (await db.pendingProductBulkUpdates.AsNoTracking().SingleAsync()).completedCount);
    }

    private static async Task<DataContext> CreateDbAsync(CatalogWriteFault fault)
    {
        var db = new DataContext(new DbContextOptionsBuilder<DataContext>().UseSqlite("Data Source=:memory:").AddInterceptors(fault).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
