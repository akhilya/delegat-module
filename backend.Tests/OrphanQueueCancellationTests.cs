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
}
