using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class CategoryImportBackgroundServiceTests
{
    [Fact]
    public async Task Worker_WritesMappingAndFinishesStandaloneOperation()
    {
        var (worker, db, logger) = CreateWorker("category-import");

        await worker.ProcessOnceAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var job = await db.pendingCategoryImports.SingleAsync();
        Assert.True(job.status == CategoryImportStatus.Completed, job.lastError);
        Assert.NotNull(job.resultLocalCategoryId);
        Assert.True(await db.categoryMappings.AnyAsync(m => m.externalCategoryId == "236"));
        Assert.Equal("completed", logger.LastFinishStatus);
    }

    [Fact]
    public async Task Worker_DoesNotFinishProductBatchOperation()
    {
        var (worker, db, logger) = CreateWorker("batch-import");

        await worker.ProcessOnceAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var job = await db.pendingCategoryImports.SingleAsync();
        Assert.True(job.status == CategoryImportStatus.Completed, job.lastError);
        Assert.True(await db.categoryMappings.AnyAsync(m => m.externalCategoryId == "236"));
        Assert.Null(logger.LastFinishStatus);
        Assert.Equal("running", (await db.operations.SingleAsync()).status);
    }

    [Fact]
    public async Task Worker_CancelsLeftoverPausedOperation()
    {
        var (worker, db, logger) = CreateWorker("category-import");
        var op = await db.operations.SingleAsync();
        op.status = "paused";
        await db.SaveChangesAsync();

        await worker.ProcessOnceAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var job = await db.pendingCategoryImports.SingleAsync();
        Assert.Equal(CategoryImportStatus.Cancelled, job.status);
        Assert.Equal("cancelled", logger.LastFinishStatus);
        Assert.False(await db.categoryMappings.AnyAsync());
    }

    private static (
        CategoryImportBackgroundService worker,
        DataContext db,
        RecordingOperationLogger logger) CreateWorker(string operationType)
    {
        var databaseName = Guid.NewGuid().ToString();
        var db = TestDbFactory.CreateDb(databaseName);
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, adapterKey: "fake");
        var op = data.AddOperation(store.Id, type: operationType, status: "running", operationId: "op-cat");
        db.pendingCategoryImports.Add(new PendingCategoryImport
        {
            connectionId = connection.Id,
            storeId = store.Id,
            authorUserId = 1,
            operationId = op.operationId,
            externalCategoryId = "236",
            externalCategoryName = "Figurines",
            localCategoryName = "Figurines",
            status = CategoryImportStatus.Pending,
            createdAt = DateTime.UtcNow,
        });
        db.SaveChanges();

        var adapter = FakeCategoryAdapter.Create(
            root: [new ExternalCategory("236", "Figurines", HasChildren: false)],
            children: new Dictionary<string, List<ExternalCategory>>(StringComparer.Ordinal),
            fieldsByCategoryId: new Dictionary<string, List<ExternalField>>(StringComparer.Ordinal)
            {
                ["236"] =
                [
                    new ExternalField("sku", "SKU", true, "string"),
                    new ExternalField("brand", "Brand", false, "string"),
                ],
            });

        var services = new ServiceCollection();
        // Each scope gets its own context on the same store. The stop watcher
        // polls from another thread; a shared instance throws and fails the job.
        services.AddDbContext<DataContext>(o => o
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped(sp => new CategoryImportService(
            sp.GetRequiredService<DataContext>(),
            new SingleAdapterRegistry(adapter)));
        var sp = services.BuildServiceProvider();

        var logger = new RecordingOperationLogger();
        var worker = new CategoryImportBackgroundService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CategoryImportBackgroundService>.Instance,
            logger);
        return (worker, db, logger);
    }

    private sealed class SingleAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
    {
        public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
        public IMarketplaceAdapter? GetByKey(string key) =>
            key == adapter.Key ? adapter : null;
        public bool Exists(string key) => key == adapter.Key;
        public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
    }

    private sealed class RecordingOperationLogger : IOperationLogger
    {
        public string? LastFinishStatus { get; private set; }

        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null)
            => Task.CompletedTask;

        public Task LogAsync(string operationId, int storeId, string level, string message)
            => Task.CompletedTask;

        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed)
            => Task.CompletedTask;

        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null)
        {
            LastFinishStatus = status;
            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(string operationId, int storeId, string status)
            => Task.CompletedTask;
    }
}
