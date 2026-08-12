using Delegat.Models;
using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class OrphanQueueCancellationTests
{
    [Fact]
    public async Task QueryOrphanedImageDownloads_SelectsNonTerminalRowsForCancelledOps()
    {
        await using var db = TestDbFactory.CreateDb();
        db.operations.Add(new Operation
        {
            operationId = "img-cancelled",
            storeId = 1,
            authorUserId = 1,
            type = "image_download",
            title = "Images",
            status = "cancelled",
            createdAt = DateTime.UtcNow,
        });
        db.operations.Add(new Operation
        {
            operationId = "img-running",
            storeId = 1,
            authorUserId = 1,
            type = "image_download",
            title = "Images",
            status = "running",
            createdAt = DateTime.UtcNow,
        });

        db.pendingImageDownloads.AddRange(
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/a.jpg", batchId = "img-cancelled",
                status = ImageDownloadStatus.Pending,
            },
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/b.jpg", batchId = "img-cancelled",
                status = ImageDownloadStatus.Failed,
            },
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/c.jpg", batchId = "img-cancelled",
                status = ImageDownloadStatus.Completed,
            },
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/d.jpg", batchId = "img-running",
                status = ImageDownloadStatus.Pending,
            },
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/e.jpg", batchId = null,
                status = ImageDownloadStatus.Pending,
            });
        await db.SaveChangesAsync();

        var orphans = await QueueCleanupBackgroundService.QueryOrphanedImageDownloads(db).ToListAsync();

        Assert.Equal(2, orphans.Count);
        Assert.All(orphans, o => Assert.Equal("img-cancelled", o.batchId));
        Assert.DoesNotContain(orphans, o => o.status == ImageDownloadStatus.Completed);
    }

    [Fact]
    public async Task QueryOrphanedProductImports_AndExports_MatchCancelledBatchesOnly()
    {
        await using var db = TestDbFactory.CreateDb();
        db.operations.Add(new Operation
        {
            operationId = "batch-x",
            storeId = 1,
            authorUserId = 1,
            type = "import",
            title = "Import",
            status = "cancelled",
            createdAt = DateTime.UtcNow,
        });

        db.pendingProductImports.AddRange(
            new PendingProductImport
            {
                connectionId = 1, externalProductId = "a", authorUserId = 1, storeId = 1,
                batchId = "batch-x", status = ProductImportStatus.InProgress,
            },
            new PendingProductImport
            {
                connectionId = 1, externalProductId = "b", authorUserId = 1, storeId = 1,
                batchId = "batch-x", status = ProductImportStatus.Cancelled,
            });
        db.pendingProductExports.AddRange(
            new PendingProductExport
            {
                connectionId = 1, productId = 1, authorUserId = 1, storeId = 1,
                batchId = "batch-x", status = ProductExportStatus.Pending,
            },
            new PendingProductExport
            {
                connectionId = 1, productId = 2, authorUserId = 1, storeId = 1,
                batchId = "other", status = ProductExportStatus.Pending,
            });
        await db.SaveChangesAsync();

        var imports = await QueueCleanupBackgroundService.QueryOrphanedProductImports(db).ToListAsync();
        var exports = await QueueCleanupBackgroundService.QueryOrphanedProductExports(db).ToListAsync();

        Assert.Single(imports);
        Assert.Equal(ProductImportStatus.InProgress, imports[0].status);
        Assert.Single(exports);
        Assert.Equal(1, exports[0].productId);
    }

    [Fact]
    public async Task QueryOrphanedProductTransfers_SelectsPendingInProgressForCancelledOps()
    {
        await using var db = TestDbFactory.CreateDb();
        db.operations.Add(new Operation
        {
            operationId = "xfer-cancelled",
            storeId = 1,
            authorUserId = 1,
            type = "product-transfer",
            title = "Clone",
            status = "cancelled",
            batchId = "xfer-cancelled",
            batchType = "product-transfer",
            createdAt = DateTime.UtcNow,
        });
        db.operations.Add(new Operation
        {
            operationId = "xfer-running",
            storeId = 1,
            authorUserId = 1,
            type = "product-transfer",
            title = "Clone",
            status = "running",
            batchId = "xfer-running",
            batchType = "product-transfer",
            createdAt = DateTime.UtcNow,
        });

        db.pendingProductTransfers.AddRange(
            new PendingProductTransfer
            {
                sourceProductId = 1, sourceStoreId = 1, targetStoreId = 2,
                mode = "clone", authorUserId = 1, batchId = "xfer-cancelled",
                status = ProductTransferStatus.Pending, createdAt = DateTime.UtcNow,
            },
            new PendingProductTransfer
            {
                sourceProductId = 2, sourceStoreId = 1, targetStoreId = 2,
                mode = "clone", authorUserId = 1, batchId = "xfer-cancelled",
                status = ProductTransferStatus.InProgress, createdAt = DateTime.UtcNow,
            },
            new PendingProductTransfer
            {
                sourceProductId = 3, sourceStoreId = 1, targetStoreId = 2,
                mode = "clone", authorUserId = 1, batchId = "xfer-cancelled",
                status = ProductTransferStatus.Completed, createdAt = DateTime.UtcNow,
            },
            new PendingProductTransfer
            {
                sourceProductId = 4, sourceStoreId = 1, targetStoreId = 2,
                mode = "clone", authorUserId = 1, batchId = "xfer-running",
                status = ProductTransferStatus.Pending, createdAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var orphans = await QueueCleanupBackgroundService.QueryOrphanedProductTransfers(db).ToListAsync();

        Assert.Equal(2, orphans.Count);
        Assert.All(orphans, o => Assert.Equal("xfer-cancelled", o.batchId));
        Assert.DoesNotContain(orphans, o => o.status == ProductTransferStatus.Completed);
    }

    [Fact]
    public async Task QueryBatchRowsToCancel_IncludesPendingInProgressFailed_Only()
    {
        await using var db = TestDbFactory.CreateDb();
        const string batchId = "img-1";
        db.pendingImageDownloads.AddRange(
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/a.jpg", batchId = batchId,
                status = ImageDownloadStatus.Pending,
            },
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/b.jpg", batchId = batchId,
                status = ImageDownloadStatus.InProgress,
            },
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/c.jpg", batchId = batchId,
                status = ImageDownloadStatus.Failed,
            },
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/d.jpg", batchId = batchId,
                status = ImageDownloadStatus.Cancelled,
            },
            new PendingImageDownload
            {
                productId = 1, storeId = 1, authorUserId = 1,
                imageUrl = "https://example.test/e.jpg", batchId = "other",
                status = ImageDownloadStatus.Pending,
            });
        await db.SaveChangesAsync();

        var rows = await ImageDownloadBackgroundService.QueryBatchRowsToCancel(db, batchId).ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(batchId, r.batchId));
        Assert.DoesNotContain(rows, r => r.status is ImageDownloadStatus.Cancelled or ImageDownloadStatus.Completed);
    }

    [Fact]
    public async Task FinishIdleImageDownloadOps_ClosesEmptyChildWhenParentImportFinished()
    {
        await using var db = TestDbFactory.CreateDb();
        db.operations.AddRange(
            new Operation
            {
                operationId = "batch-1",
                storeId = 1,
                authorUserId = 1,
                type = "batch-import",
                title = "Import",
                status = "completed",
                batchId = "batch-1",
                batchType = "import",
                createdAt = DateTime.UtcNow.AddHours(-2),
                finishedAt = DateTime.UtcNow.AddHours(-1),
            },
            new Operation
            {
                operationId = "batch-1-img",
                storeId = 1,
                authorUserId = 1,
                type = "image-download",
                title = "Image download",
                status = "running",
                batchId = "batch-1-img",
                batchType = "image-download",
                totalItems = 0,
                createdAt = DateTime.UtcNow.AddHours(-2),
            },
            new Operation
            {
                operationId = "batch-2",
                storeId = 1,
                authorUserId = 1,
                type = "batch-import",
                title = "Import still going",
                status = "running",
                batchId = "batch-2",
                batchType = "import",
                createdAt = DateTime.UtcNow,
            },
            new Operation
            {
                operationId = "batch-2-img",
                storeId = 1,
                authorUserId = 1,
                type = "image-download",
                title = "Image download",
                status = "running",
                batchId = "batch-2-img",
                batchType = "image-download",
                totalItems = 0,
                createdAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var logger = new DbFinishOperationLogger(db);
        var closed = await ImageDownloadBackgroundService.FinishIdleImageDownloadOpsAsync(
            db, logger, CancellationToken.None);

        Assert.Equal(1, closed);
        var idle = await db.operations.SingleAsync(o => o.operationId == "batch-1-img");
        Assert.Equal("completed", idle.status);
        Assert.Equal("No images to download", idle.summary);
        Assert.NotNull(idle.finishedAt);

        var waiting = await db.operations.SingleAsync(o => o.operationId == "batch-2-img");
        Assert.Equal("running", waiting.status);
        Assert.Null(waiting.finishedAt);
    }

    [Fact]
    public async Task FinishIdleImageDownloadOps_KeepsChildWhileQueueHasActiveWork()
    {
        await using var db = TestDbFactory.CreateDb();
        db.operations.AddRange(
            new Operation
            {
                operationId = "batch-3",
                storeId = 1,
                authorUserId = 1,
                type = "batch-import",
                title = "Import",
                status = "completed",
                batchId = "batch-3",
                batchType = "import",
                createdAt = DateTime.UtcNow.AddHours(-1),
                finishedAt = DateTime.UtcNow,
            },
            new Operation
            {
                operationId = "batch-3-img",
                storeId = 1,
                authorUserId = 1,
                type = "image-download",
                title = "Image download",
                status = "running",
                batchId = "batch-3-img",
                batchType = "image-download",
                createdAt = DateTime.UtcNow.AddHours(-1),
            });
        db.pendingImageDownloads.Add(new PendingImageDownload
        {
            productId = 1,
            storeId = 1,
            authorUserId = 1,
            imageUrl = "https://example.test/a.jpg",
            batchId = "batch-3-img",
            status = ImageDownloadStatus.Pending,
        });
        await db.SaveChangesAsync();

        var logger = new DbFinishOperationLogger(db);
        var closed = await ImageDownloadBackgroundService.FinishIdleImageDownloadOpsAsync(
            db, logger, CancellationToken.None);

        Assert.Equal(0, closed);
        var op = await db.operations.SingleAsync(o => o.operationId == "batch-3-img");
        Assert.Equal("running", op.status);
    }

    private sealed class DbFinishOperationLogger : IOperationLogger
    {
        private readonly DataContext _db;

        public DbFinishOperationLogger(DataContext db) => _db = db;

        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null) =>
            Task.CompletedTask;

        public Task LogAsync(string operationId, int storeId, string level, string message) =>
            Task.CompletedTask;

        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) =>
            Task.CompletedTask;

        public async Task FinishAsync(string operationId, int storeId, string status, string? summary = null)
        {
            var op = await _db.operations.FirstAsync(o => o.operationId == operationId);
            op.status = status;
            op.summary = summary;
            op.finishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        public Task UpdateStatusAsync(string operationId, int storeId, string status) =>
            Task.CompletedTask;
    }
}
