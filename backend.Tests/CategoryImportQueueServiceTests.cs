using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class CategoryImportQueueServiceTests
{
    [Fact]
    public async Task Enqueue_SkipsWhenMappingExists()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        var category = data.AddCategory(store.Id);
        data.AddMapping(connection.Id, category.Id, "236");
        await db.SaveChangesAsync();

        var queue = new CategoryImportQueueService(db);
        var job = await queue.EnqueueIfNeededAsync(
            connection, "236", "Toys", "Toys", null, null, "op1", 1);

        Assert.Null(job);
        Assert.Empty(db.pendingCategoryImports);
    }

    [Fact]
    public async Task Enqueue_DoesNotCreateCategoryOrMapping()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        await db.SaveChangesAsync();

        var queue = new CategoryImportQueueService(db);
        var job = await queue.EnqueueIfNeededAsync(
            connection, "236", "Figurines", "Figurines", null, null, "op1", 1);

        Assert.NotNull(job);
        Assert.Equal(CategoryImportStatus.Pending, job.status);
        Assert.Empty(db.categories);
        Assert.Empty(db.categoryMappings);
        Assert.Equal(1, await db.pendingCategoryImports.CountAsync());
    }

    [Fact]
    public async Task Enqueue_ReusesInFlightJobForSameExternalCategory()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        await db.SaveChangesAsync();

        var queue = new CategoryImportQueueService(db);
        var first = await queue.EnqueueIfNeededAsync(
            connection, "236", "A", "A", null, null, "op1", 1);
        var second = await queue.EnqueueIfNeededAsync(
            connection, "236", "B", "B", null, null, "op1", 1);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.pendingCategoryImports.CountAsync());
    }

    [Fact]
    public async Task Enqueue_ResetsFailedJob()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        await db.SaveChangesAsync();

        db.pendingCategoryImports.Add(new PendingCategoryImport
        {
            connectionId = connection.Id,
            storeId = store.Id,
            authorUserId = 1,
            operationId = "old",
            externalCategoryId = "236",
            status = CategoryImportStatus.Failed,
            retryCount = 3,
            lastError = "boom",
            createdAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var queue = new CategoryImportQueueService(db);
        var job = await queue.EnqueueIfNeededAsync(
            connection, "236", "Toys", "Toys", null, null, "op-new", 1);

        Assert.NotNull(job);
        Assert.Equal(CategoryImportStatus.Pending, job.status);
        Assert.Equal(0, job.retryCount);
        Assert.Equal("op-new", job.operationId);
        Assert.Null(job.lastError);
        Assert.Equal(1, await db.pendingCategoryImports.CountAsync());
    }

    [Fact]
    public async Task Enqueue_WithLocalCategory_DoesNotSkipMappingOnOtherLocalCategory()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id);
        var mapped = data.AddCategory(store.Id, "Mapped");
        var target = data.AddCategory(store.Id, "Target");
        data.AddMapping(connection.Id, mapped.Id, "236");
        await db.SaveChangesAsync();

        var queue = new CategoryImportQueueService(db);
        var job = await queue.EnqueueIfNeededAsync(
            connection, "236", "Toys", "Toys", target.Id, null, "op1", 1);

        Assert.NotNull(job);
        Assert.Equal(target.Id, job.localCategoryId);
        Assert.Equal(1, await db.pendingCategoryImports.CountAsync());
    }
}
