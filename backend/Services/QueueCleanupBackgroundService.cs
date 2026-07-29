using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Configuration;
using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// Deletes terminal queue rows and operations older than QUEUE_RETENTION_DAYS (default 7).
/// Also marks orphaned InProgress rows as Cancelled when their operation is cancelled/missing.
/// </summary>
public sealed class QueueCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StaleInProgressAge = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueueCleanupBackgroundService> _logger;

    public QueueCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<QueueCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay first run slightly after startup
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Queue cleanup failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task CleanupOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var retentionDays = FeatureFlags.GetQueueRetentionDays();
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var staleBefore = DateTime.UtcNow - StaleInProgressAge;

        // Pending/InProgress/Failed rows whose operation is already cancelled would otherwise
        // be re-polled forever (retention cleanup only deletes terminal queue statuses).
        var orphanedImages = await QueryOrphanedImageDownloads(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Cancelled), ct);

        var orphanedImports = await QueryOrphanedProductImports(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductImportStatus.Cancelled), ct);

        var orphanedExports = await QueryOrphanedProductExports(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Cancelled), ct);

        if (orphanedImages + orphanedImports + orphanedExports > 0)
        {
            _logger.LogInformation(
                "Queue cleanup cancelled orphan rows: images={Images}, imports={Imports}, exports={Exports}",
                orphanedImages, orphanedImports, orphanedExports);
        }

        // Cancel stale InProgress whose operation is cancelled/missing
        var staleExportIds = await db.pendingProductExports
            .Where(p => p.status == ProductExportStatus.InProgress
                        && (p.lastAttemptAt ?? p.createdAt) < staleBefore)
            .Select(p => new { p.Id, p.batchId })
            .Take(2000)
            .ToListAsync(ct);

        foreach (var group in staleExportIds.GroupBy(x => x.batchId))
        {
            var opStatus = group.Key == null
                ? null
                : await db.operations.AsNoTracking()
                    .Where(o => o.operationId == group.Key)
                    .Select(o => o.status)
                    .FirstOrDefaultAsync(ct);

            if (group.Key == null || opStatus is null or "cancelled" or "failed" or "completed" or "partial")
            {
                var ids = group.Select(g => g.Id).ToList();
                await db.pendingProductExports
                    .Where(p => ids.Contains(p.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Cancelled), ct);
            }
        }

        var staleImportIds = await db.pendingProductImports
            .Where(p => p.status == ProductImportStatus.InProgress
                        && (p.lastAttemptAt ?? p.createdAt) < staleBefore)
            .Select(p => new { p.Id, p.batchId })
            .Take(2000)
            .ToListAsync(ct);

        foreach (var group in staleImportIds.GroupBy(x => x.batchId))
        {
            var opStatus = group.Key == null
                ? null
                : await db.operations.AsNoTracking()
                    .Where(o => o.operationId == group.Key)
                    .Select(o => o.status)
                    .FirstOrDefaultAsync(ct);

            if (group.Key == null || opStatus is null or "cancelled" or "failed" or "completed" or "partial")
            {
                var ids = group.Select(g => g.Id).ToList();
                await db.pendingProductImports
                    .Where(p => ids.Contains(p.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductImportStatus.Cancelled), ct);
            }
        }

        var deletedExports = await db.pendingProductExports
            .Where(p => (p.status == ProductExportStatus.Completed
                         || p.status == ProductExportStatus.Failed
                         || p.status == ProductExportStatus.Cancelled)
                        && p.createdAt < cutoff)
            .ExecuteDeleteAsync(ct);

        var deletedImports = await db.pendingProductImports
            .Where(p => (p.status == ProductImportStatus.Completed
                         || p.status == ProductImportStatus.Failed
                         || p.status == ProductImportStatus.Cancelled)
                        && p.createdAt < cutoff)
            .ExecuteDeleteAsync(ct);

        var deletedImages = await db.pendingImageDownloads
            .Where(p => (p.status == ImageDownloadStatus.Completed
                         || p.status == ImageDownloadStatus.Failed
                         || p.status == ImageDownloadStatus.Cancelled)
                        && p.createdAt < cutoff)
            .ExecuteDeleteAsync(ct);

        var deletedTransfers = await db.pendingProductTransfers
            .Where(p => (p.status == ProductTransferStatus.Completed
                         || p.status == ProductTransferStatus.Failed
                         || p.status == ProductTransferStatus.Cancelled)
                        && p.createdAt < cutoff)
            .ExecuteDeleteAsync(ct);

        var deletedBulkUpdates = await db.pendingProductBulkUpdates
            .Where(p => (p.status == ProductBulkUpdateStatus.Completed
                         || p.status == ProductBulkUpdateStatus.Failed
                         || p.status == ProductBulkUpdateStatus.Cancelled
                         || p.status == ProductBulkUpdateStatus.Partial)
                        && p.createdAt < cutoff)
            .ExecuteDeleteAsync(ct);

        var deletedFolderJobs = await db.pendingFolderJobs
            .Where(p => (p.status == FolderJobStatus.Completed
                         || p.status == FolderJobStatus.Failed
                         || p.status == FolderJobStatus.Cancelled)
                        && p.createdAt < cutoff
                        && p.resultObjectKey == null)
            .ExecuteDeleteAsync(ct);

        var deletedOps = await db.operations
            .Where(o => (o.status == "completed"
                         || o.status == "failed"
                         || o.status == "cancelled"
                         || o.status == "partial")
                        && (o.finishedAt ?? o.createdAt) < cutoff)
            .ExecuteDeleteAsync(ct);

        var total = deletedExports + deletedImports + deletedImages + deletedTransfers + deletedBulkUpdates + deletedFolderJobs + deletedOps;
        if (total > 0)
        {
            _logger.LogInformation(
                "Queue cleanup removed exports={Exports}, imports={Imports}, images={Images}, transfers={Transfers}, bulkUpdates={BulkUpdates}, folderJobs={FolderJobs}, operations={Ops} older than {Days}d",
                deletedExports, deletedImports, deletedImages, deletedTransfers, deletedBulkUpdates, deletedFolderJobs, deletedOps, retentionDays);
        }
    }

    /// <summary>
    /// Non-terminal image queue rows tied to a cancelled operation (would be re-polled forever).
    /// </summary>
    internal static IQueryable<PendingImageDownload> QueryOrphanedImageDownloads(DataContext db) =>
        db.pendingImageDownloads.Where(p =>
            (p.status == ImageDownloadStatus.Pending
             || p.status == ImageDownloadStatus.InProgress
             || p.status == ImageDownloadStatus.Failed)
            && p.batchId != null
            && db.operations.Any(o => o.operationId == p.batchId && o.status == "cancelled"));

    /// <summary>
    /// Non-terminal import queue rows tied to a cancelled operation.
    /// </summary>
    internal static IQueryable<PendingProductImport> QueryOrphanedProductImports(DataContext db) =>
        db.pendingProductImports.Where(p =>
            (p.status == ProductImportStatus.Pending
             || p.status == ProductImportStatus.InProgress
             || p.status == ProductImportStatus.Failed)
            && p.batchId != null
            && db.operations.Any(o => o.operationId == p.batchId && o.status == "cancelled"));

    /// <summary>
    /// Non-terminal export queue rows tied to a cancelled operation.
    /// </summary>
    internal static IQueryable<PendingProductExport> QueryOrphanedProductExports(DataContext db) =>
        db.pendingProductExports.Where(p =>
            (p.status == ProductExportStatus.Pending
             || p.status == ProductExportStatus.InProgress
             || p.status == ProductExportStatus.Failed)
            && p.batchId != null
            && db.operations.Any(o => o.operationId == p.batchId && o.status == "cancelled"));
}
