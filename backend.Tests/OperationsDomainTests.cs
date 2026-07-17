using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class OperationsDomainTests
{
    private static (DataContext db, TestDataBuilder data, FakeCurrentUserService currentUser) Create()
    {
        var db = TestDbFactory.CreateDb();
        return (db, new TestDataBuilder(db), new FakeCurrentUserService());
    }

    [Fact]
    public async Task AddOperation_PersistsRequiredDomainFields()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var operation = data.AddOperation(store.Id, authorUserId: 12, type: "batch-export",
            status: "running", operationId: "operation-1", title: "Export products");
        await data.SaveAsync();

        var saved = await db.operations.SingleAsync();

        Assert.Equal(operation.operationId, saved.operationId);
        Assert.Equal(store.Id, saved.storeId);
        Assert.Equal(12, saved.authorUserId);
        Assert.Equal("batch-export", saved.type);
        Assert.Equal("Export products", saved.title);
        Assert.Equal("running", saved.status);
        Assert.NotEqual(default, saved.createdAt);
        Assert.Null(saved.finishedAt);
    }

    [Fact]
    public async Task OperationsFilter_ReturnsOnlySelectedStore()
    {
        var (db, data, currentUser) = Create();
        var first = data.AddStore();
        var second = data.AddStore();
        currentUser.SelectedStoreId = first.Id;
        var own = data.AddOperation(first.Id);
        data.AddOperation(second.Id);
        await data.SaveAsync();

        var visible = await db.operations
            .Where(o => o.storeId == currentUser.GetSelectedStoreId())
            .ToListAsync();

        Assert.Equal(new[] { own.operationId }, visible.Select(o => o.operationId));
    }

    [Fact]
    public async Task OperationsFilter_ReturnsOnlyRequestedStatus()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        data.AddOperation(store.Id, status: "running");
        var completed = data.AddOperation(store.Id, status: "completed");
        data.AddOperation(store.Id, status: "failed");
        await data.SaveAsync();

        var result = await db.operations.Where(o => o.storeId == store.Id && o.status == "completed").ToListAsync();

        Assert.Equal(new[] { completed.operationId }, result.Select(o => o.operationId));
    }

    [Fact]
    public async Task OperationsFilter_CombinesStoreAndStatus()
    {
        var (db, data, _) = Create();
        var first = data.AddStore();
        var second = data.AddStore();
        var expected = data.AddOperation(first.Id, status: "failed");
        data.AddOperation(first.Id, status: "completed");
        data.AddOperation(second.Id, status: "failed");
        await data.SaveAsync();

        var result = await db.operations
            .Where(o => o.storeId == first.Id && o.status == "failed")
            .ToListAsync();

        Assert.Equal(new[] { expected.operationId }, result.Select(o => o.operationId));
    }

    [Fact]
    public async Task FinishOperation_CompletedSetsFinishedAt()
    {
        var (db, data, _) = Create();
        var operation = data.AddOperation(data.AddStore().Id);
        await data.SaveAsync();

        operation.status = "completed";
        operation.summary = "All 10 products exported";
        operation.finishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var saved = await db.operations.SingleAsync();
        Assert.Equal("completed", saved.status);
        Assert.Equal("All 10 products exported", saved.summary);
        Assert.NotNull(saved.finishedAt);
    }

    [Fact]
    public async Task FinishOperation_FailedSetsFinishedAt()
    {
        var (db, data, _) = Create();
        var operation = data.AddOperation(data.AddStore().Id);
        await data.SaveAsync();

        operation.status = "failed";
        operation.summary = "Connection timed out";
        operation.finishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var saved = await db.operations.SingleAsync();
        Assert.Equal("failed", saved.status);
        Assert.Equal("Connection timed out", saved.summary);
        Assert.NotNull(saved.finishedAt);
    }

    [Fact]
    public async Task FinishOperation_LeavesCreationTimestampUntouched()
    {
        var (db, data, _) = Create();
        var operation = data.AddOperation(data.AddStore().Id);
        operation.createdAt = DateTime.UtcNow.AddMinutes(-5);
        await data.SaveAsync();
        var startedAt = operation.createdAt;

        operation.status = "completed";
        operation.finishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Equal(startedAt, (await db.operations.SingleAsync()).createdAt);
    }

    [Fact]
    public async Task OperationLogEntry_PersistsRequiredFields()
    {
        var (db, data, _) = Create();
        var operation = data.AddOperation(data.AddStore().Id, operationId: "log-operation");
        await data.SaveAsync();
        db.operationLogEntries.Add(new OperationLogEntry
        {
            operationId = operation.operationId, level = "warning", message = "Retrying request",
            timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var entry = await db.operationLogEntries.SingleAsync();
        Assert.Equal("log-operation", entry.operationId);
        Assert.Equal("warning", entry.level);
        Assert.Equal("Retrying request", entry.message);
        Assert.NotEqual(default, entry.timestamp);
    }

    [Fact]
    public async Task OperationLogs_FilterByOperationId()
    {
        var (db, data, _) = Create();
        var first = data.AddOperation(data.AddStore().Id, operationId: "first");
        var second = data.AddOperation(1, operationId: "second");
        await data.SaveAsync();
        db.operationLogEntries.AddRange(
            new OperationLogEntry { operationId = first.operationId, level = "info", message = "one", timestamp = DateTime.UtcNow },
            new OperationLogEntry { operationId = second.operationId, level = "info", message = "two", timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var logs = await db.operationLogEntries.Where(e => e.operationId == first.operationId).ToListAsync();

        Assert.Single(logs);
        Assert.Equal("one", logs[0].message);
    }

    [Fact]
    public async Task OperationLogs_AreOrderedByTimestamp()
    {
        var (db, data, _) = Create();
        var operation = data.AddOperation(data.AddStore().Id);
        await data.SaveAsync();
        var earlier = DateTime.UtcNow.AddMinutes(-1);
        var later = DateTime.UtcNow;
        db.operationLogEntries.AddRange(
            new OperationLogEntry { operationId = operation.operationId, level = "info", message = "later", timestamp = later },
            new OperationLogEntry { operationId = operation.operationId, level = "info", message = "earlier", timestamp = earlier });
        await db.SaveChangesAsync();

        var messages = await db.operationLogEntries.Where(e => e.operationId == operation.operationId)
            .OrderBy(e => e.timestamp).Select(e => e.message).ToListAsync();

        Assert.Equal(new[] { "earlier", "later" }, messages);
    }

    [Fact]
    public async Task CrossStoreIsolation_DoesNotRevealForeignOperationOrLogs()
    {
        var (db, data, currentUser) = Create();
        var first = data.AddStore();
        var second = data.AddStore();
        currentUser.SelectedStoreId = first.Id;
        var own = data.AddOperation(first.Id, operationId: "own");
        var foreign = data.AddOperation(second.Id, operationId: "foreign");
        await data.SaveAsync();
        db.operationLogEntries.AddRange(
            new OperationLogEntry { operationId = own.operationId, level = "info", message = "own log", timestamp = DateTime.UtcNow },
            new OperationLogEntry { operationId = foreign.operationId, level = "info", message = "foreign log", timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var operationIds = await db.operations.Where(o => o.storeId == currentUser.GetSelectedStoreId())
            .Select(o => o.operationId).ToListAsync();
        var logs = await db.operationLogEntries.Where(e => operationIds.Contains(e.operationId)).ToListAsync();

        Assert.Equal(new[] { "own" }, operationIds);
        Assert.Equal(new[] { "own log" }, logs.Select(l => l.message));
    }

    [Fact]
    public async Task DeleteOperation_RemovesItsLogsAndKeepsOtherOperations()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var remove = data.AddOperation(store.Id, operationId: "remove");
        var keep = data.AddOperation(store.Id, operationId: "keep");
        await data.SaveAsync();
        db.operationLogEntries.AddRange(
            new OperationLogEntry { operationId = remove.operationId, level = "info", message = "remove", timestamp = DateTime.UtcNow },
            new OperationLogEntry { operationId = keep.operationId, level = "info", message = "keep", timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.operationLogEntries.RemoveRange(db.operationLogEntries.Where(e => e.operationId == remove.operationId));
        db.operations.Remove(remove);
        await db.SaveChangesAsync();

        Assert.Equal(new[] { "keep" }, await db.operations.Select(o => o.operationId).ToListAsync());
        Assert.Equal(new[] { "keep" }, await db.operationLogEntries.Select(e => e.operationId).ToListAsync());
    }

    [Fact]
    public async Task BulkDeleteSimulation_RemovesOnlySelectedStoreOperations()
    {
        var (db, data, currentUser) = Create();
        var first = data.AddStore();
        var second = data.AddStore();
        currentUser.SelectedStoreId = first.Id;
        var remove = data.AddOperation(first.Id, operationId: "remove");
        data.AddOperation(first.Id, operationId: "keep");
        data.AddOperation(second.Id, operationId: "foreign");
        await data.SaveAsync();

        var idsToDelete = new[] { remove.operationId, "foreign" };
        var delete = await db.operations
            .Where(o => idsToDelete.Contains(o.operationId) && o.storeId == currentUser.GetSelectedStoreId())
            .ToListAsync();
        db.operations.RemoveRange(delete);
        await db.SaveChangesAsync();

        Assert.Equal(new[] { "foreign", "keep" }, await db.operations.OrderBy(o => o.operationId).Select(o => o.operationId).ToListAsync());
    }

    [Fact]
    public async Task BulkDeleteSimulation_RemovesLogsForSelectedOperations()
    {
        var (db, data, currentUser) = Create();
        var store = data.AddStore();
        currentUser.SelectedStoreId = store.Id;
        var remove = data.AddOperation(store.Id, operationId: "remove");
        var keep = data.AddOperation(store.Id, operationId: "keep");
        await data.SaveAsync();
        db.operationLogEntries.AddRange(
            new OperationLogEntry { operationId = remove.operationId, level = "info", message = "remove log", timestamp = DateTime.UtcNow },
            new OperationLogEntry { operationId = keep.operationId, level = "info", message = "keep log", timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var idsToDelete = new[] { remove.operationId };
        db.operationLogEntries.RemoveRange(db.operationLogEntries.Where(e => idsToDelete.Contains(e.operationId)));
        db.operations.RemoveRange(db.operations.Where(o => idsToDelete.Contains(o.operationId) && o.storeId == currentUser.GetSelectedStoreId()));
        await db.SaveChangesAsync();

        Assert.Equal(new[] { "keep" }, await db.operationLogEntries.Select(e => e.operationId).ToListAsync());
        Assert.Equal(new[] { "keep" }, await db.operations.Select(o => o.operationId).ToListAsync());
    }

    [Fact]
    public async Task BulkDeleteSimulation_LeavesUnselectedStatusRecords()
    {
        var (db, data, _) = Create();
        var store = data.AddStore();
        var completed = data.AddOperation(store.Id, status: "completed");
        var failed = data.AddOperation(store.Id, status: "failed");
        await data.SaveAsync();

        db.operations.RemoveRange(db.operations.Where(o => o.status == "completed"));
        await db.SaveChangesAsync();

        Assert.Equal(new[] { failed.operationId }, await db.operations.Select(o => o.operationId).ToListAsync());
        Assert.DoesNotContain(await db.operations.ToListAsync(), o => o.operationId == completed.operationId);
    }
}
