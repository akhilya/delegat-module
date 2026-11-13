using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Configuration;
using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// Deletes terminal queue rows and operations older than QUEUE_RETENTION_DAYS (default 7).
/// Also marks orphaned InProgress rows as Cancelled when their operation is cancelled/missing,
/// abandons stuck client-side operations, and fails ancient running ops as a safety net.
/// </summary>
public sealed class QueueCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StaleInProgressAge = TimeSpan.FromHours(24);
    /// <summary>Client file-upload/delete left running after tab close (upload HTTP timeout is 15m).</summary>
    private static readonly TimeSpan AbandonedClientOpAge = TimeSpan.FromMinutes(20);
    /// <summary>Any operation stuck in running/paused with no finish.</summary>
    private static readonly TimeSpan AncientRunningOpAge = TimeSpan.FromHours(24);

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
        var operationLogger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();
        var retentionDays = FeatureFlags.GetQueueRetentionDays();
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var staleBefore = DateTime.UtcNow - StaleInProgressAge;
        var abandonedClientBefore = DateTime.UtcNow - AbandonedClientOpAge;
        var ancientRunningBefore = DateTime.UtcNow - AncientRunningOpAge;

        // Pending/InProgress/Failed rows whose operation is already cancelled would otherwise
        // be re-polled forever (retention cleanup only deletes terminal queue statuses).
        var orphanedImages = await QueryOrphanedImageDownloads(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Cancelled), ct);

        var orphanedImports = await QueryOrphanedProductImports(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductImportStatus.Cancelled), ct);

        var orphanedExports = await QueryOrphanedProductExports(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Cancelled), ct);

        var orphanedTransfers = await QueryOrphanedProductTransfers(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductTransferStatus.Cancelled), ct);

        var orphanedBulkUpdates = await QueryOrphanedProductBulkUpdates(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductBulkUpdateStatus.Cancelled), ct);

        var orphanedFolderJobs = await QueryOrphanedFolderJobs(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, FolderJobStatus.Cancelled), ct);

        var orphanedCategoryImports = await QueryOrphanedCategoryImports(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, CategoryImportStatus.Cancelled), ct);

        var orphanedCatalogConnect = await QueryOrphanedCatalogConnectJobs(db)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, CatalogConnectJobStatus.Cancelled), ct);

        if (orphanedImages + orphanedImports + orphanedExports + orphanedTransfers + orphanedBulkUpdates + orphanedFolderJobs + orphanedCategoryImports + orphanedCatalogConnect > 0)
        {
            _logger.LogInformation(
                "Queue cleanup cancelled orphan rows: images={Images}, imports={Imports}, exports={Exports}, transfers={Transfers}, bulk={Bulk}, folders={Folders}, categories={Categories}, catalogConnect={CatalogConnect}",
                orphanedImages, orphanedImports, orphanedExports, orphanedTransfers, orphanedBulkUpdates, orphanedFolderJobs, orphanedCategoryImports, orphanedCatalogConnect);
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

        // Abandon client-side ops (file-upload / file-delete) left running after tab close.
        var abandonedClientOps = await db.operations
            .Where(o => (o.status == "running" || o.status == "paused")
                        && (o.batchType == "client"
                            || o.type == "file-upload"
                            || o.type == "file-delete")
                        && o.createdAt < abandonedClientBefore)
            .Select(o => new { o.operationId, o.storeId })
            .Take(500)
            .ToListAsync(ct);

        foreach (var op in abandonedClientOps)
        {
            await operationLogger.LogAsync(op.operationId, op.storeId, "warning",
                "Abandoned: no client activity (tab closed or upload stalled)");
            await operationLogger.FinishAsync(op.operationId, op.storeId, "failed",
                "Abandoned: client disconnected or upload stalled");
        }

        if (abandonedClientOps.Count > 0)
            _logger.LogInformation("Queue cleanup abandoned {Count} client operations", abandonedClientOps.Count);

        // Safety net: any running/paused op older than 24h without finish.
        var ancientOps = await db.operations
            .Where(o => (o.status == "running" || o.status == "paused")
                        && o.createdAt < ancientRunningBefore)
            .Select(o => new { o.operationId, o.storeId, o.batchId, o.batchType })
            .Take(500)
            .ToListAsync(ct);

        foreach (var op in ancientOps)
        {
            if (op.batchId != null && op.batchType == "product-transfer")
            {
                await db.pendingProductTransfers
                    .Where(p => p.batchId == op.batchId
                                && (p.status == ProductTransferStatus.Pending
                                    || p.status == ProductTransferStatus.InProgress))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductTransferStatus.Cancelled), ct);
            }

            await operationLogger.LogAsync(op.operationId, op.storeId, "warning",
                "Timed out after 24h with no completion");
            await operationLogger.FinishAsync(op.operationId, op.storeId, "failed",
                "Timed out after 24h with no completion");
        }

        if (ancientOps.Count > 0)
            _logger.LogInformation("Queue cleanup timed out {Count} ancient running operations", ancientOps.Count);

        // Empty / finished image-download children left running after import (no queue work).
        var idleImages = await ImageDownloadBackgroundService.FinishIdleImageDownloadOpsAsync(
            db, operationLogger, ct);
        if (idleImages > 0)
            _logger.LogInformation("Queue cleanup closed {Count} idle image-download operation(s)", idleImages);

        var deletedExports = await DeleteInBatchesAsync(db.pendingProductExports
            .Where(p => (p.status == ProductExportStatus.Completed
                         || p.status == ProductExportStatus.Failed
                         || p.status == ProductExportStatus.Cancelled)
                         && p.createdAt < cutoff), ct);

        var deletedImports = await DeleteInBatchesAsync(db.pendingProductImports
            .Where(p => (p.status == ProductImportStatus.Completed
                         || p.status == ProductImportStatus.Failed
                         || p.status == ProductImportStatus.Cancelled)
                         && p.createdAt < cutoff), ct);

        var deletedImages = await DeleteInBatchesAsync(db.pendingImageDownloads
            .Where(p => (p.status == ImageDownloadStatus.Completed
                         || p.status == ImageDownloadStatus.Failed
                         || p.status == ImageDownloadStatus.Cancelled)
                         && p.createdAt < cutoff), ct);

        var deletedTransfers = await DeleteInBatchesAsync(db.pendingProductTransfers
            .Where(p => (p.status == ProductTransferStatus.Completed
                         || p.status == ProductTransferStatus.Failed
                         || p.status == ProductTransferStatus.Cancelled
                         || p.status == ProductTransferStatus.Partial)
                         && p.createdAt < cutoff), ct);

        var deletedBulkUpdates = await DeleteInBatchesAsync(db.pendingProductBulkUpdates
            .Where(p => (p.status == ProductBulkUpdateStatus.Completed
                         || p.status == ProductBulkUpdateStatus.Failed
                         || p.status == ProductBulkUpdateStatus.Cancelled
                         || p.status == ProductBulkUpdateStatus.Partial)
                         && p.createdAt < cutoff), ct);

        var deletedFolderJobs = await DeleteInBatchesAsync(db.pendingFolderJobs
            .Where(p => (p.status == FolderJobStatus.Completed
                         || p.status == FolderJobStatus.Failed
                         || p.status == FolderJobStatus.Cancelled)
                        && p.createdAt < cutoff
                         && p.resultObjectKey == null), ct);

        var deletedCategoryImports = await DeleteInBatchesAsync(db.pendingCategoryImports
            .Where(p => (p.status == CategoryImportStatus.Completed
                         || p.status == CategoryImportStatus.Failed
                         || p.status == CategoryImportStatus.Cancelled)
                         && p.createdAt < cutoff), ct);

        var deletedCatalogJobs = await DeleteInBatchesAsync(db.pendingCatalogConnectJobs.Where(p =>
            (p.status == CatalogConnectJobStatus.Completed || p.status == CatalogConnectJobStatus.Failed
             || p.status == CatalogConnectJobStatus.Cancelled) && p.createdAt < cutoff), ct);
        var deletedOps = await DeleteTerminalOperationsAsync(db, cutoff, ct);
        // Older versions removed operation parents without their string-keyed log rows.
        var deletedOrphanLogs = await DeleteInBatchesAsync(db.operationLogEntries.Where(e => e.timestamp < cutoff
            && !db.operations.Any(o => o.operationId == e.operationId)), ct);

        var total = deletedExports + deletedImports + deletedImages + deletedTransfers + deletedBulkUpdates + deletedFolderJobs + deletedCategoryImports + deletedCatalogJobs + deletedOps + deletedOrphanLogs;
        if (total > 0)
        {
            _logger.LogInformation(
                "Queue cleanup removed exports={Exports}, imports={Imports}, images={Images}, transfers={Transfers}, bulkUpdates={BulkUpdates}, folderJobs={FolderJobs}, categoryImports={CategoryImports}, catalogJobs={CatalogJobs}, operations={Ops}, orphanLogs={OrphanLogs} older than {Days}d",
                deletedExports, deletedImports, deletedImages, deletedTransfers, deletedBulkUpdates, deletedFolderJobs, deletedCategoryImports, deletedCatalogJobs, deletedOps, deletedOrphanLogs, retentionDays);
        }
    }

    private const int CleanupBatchSize = 500;

    internal static async Task<int> DeleteInBatchesAsync<TEntity>(IQueryable<TEntity> query, CancellationToken ct)
        where TEntity : class
    {
        var deleted = 0;
        while (true)
        {
            var ids = await query.AsNoTracking().OrderBy(e => EF.Property<int>(e, "Id"))
                .Select(e => EF.Property<int>(e, "Id")).Take(CleanupBatchSize).ToListAsync(ct);
            if (ids.Count == 0) return deleted;
            var count = await query.Where(e => ids.Contains(EF.Property<int>(e, "Id"))).ExecuteDeleteAsync(ct);
            deleted += count;
            if (count == 0) return deleted;
            // Release the connection and give interactive requests room between committed batches.
            await Task.Delay(25, ct);
        }
    }

    internal static async Task<int> DeleteTerminalOperationsAsync(DataContext db, DateTime cutoff, CancellationToken ct)
    {
        var deleted = 0;
        while (true)
        {
            var count = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var rows = await db.operations.AsNoTracking().Where(o =>
                        (o.status == "completed" || o.status == "failed" || o.status == "cancelled" || o.status == "partial")
                        && (o.finishedAt ?? o.createdAt) < cutoff)
                    .OrderBy(o => o.Id).Take(CleanupBatchSize)
                    .Select(o => new { o.Id, o.operationId }).ToListAsync(ct);
                if (rows.Count == 0) return 0;
                var keys = rows.Select(o => o.operationId).ToList();
                var ids = rows.Select(o => o.Id).ToList();
                await db.operationLogEntries.Where(e => keys.Contains(e.operationId)).ExecuteDeleteAsync(ct);
                var removed = await db.operations.Where(o => ids.Contains(o.Id)).ExecuteDeleteAsync(ct);
                await transaction.CommitAsync(ct);
                return removed;
            });
            deleted += count;
            if (count == 0) return deleted;
            await Task.Delay(25, ct);
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

    internal static IQueryable<PendingProductTransfer> QueryOrphanedProductTransfers(DataContext db) =>
        db.pendingProductTransfers.Where(p =>
            (p.status == ProductTransferStatus.Pending
             || p.status == ProductTransferStatus.InProgress)
            && p.batchId != null
            && db.operations.Any(o => o.operationId == p.batchId && o.status == "cancelled"));

    internal static IQueryable<PendingProductBulkUpdate> QueryOrphanedProductBulkUpdates(DataContext db) =>
        db.pendingProductBulkUpdates.Where(p =>
            (p.status == ProductBulkUpdateStatus.Pending
             || p.status == ProductBulkUpdateStatus.InProgress)
            && p.batchId != null
            && db.operations.Any(o => o.operationId == p.batchId && o.status == "cancelled"));

    internal static IQueryable<PendingFolderJob> QueryOrphanedFolderJobs(DataContext db) =>
        db.pendingFolderJobs.Where(p =>
            (p.status == FolderJobStatus.Pending
             || p.status == FolderJobStatus.InProgress
             || p.status == FolderJobStatus.Failed)
            && db.operations.Any(o => o.operationId == p.operationId && o.status == "cancelled"));

    internal static IQueryable<PendingCategoryImport> QueryOrphanedCategoryImports(DataContext db) =>
        db.pendingCategoryImports.Where(p =>
            (p.status == CategoryImportStatus.Pending
             || p.status == CategoryImportStatus.InProgress
             || p.status == CategoryImportStatus.Failed)
            && db.operations.Any(o => o.operationId == p.operationId && o.status == "cancelled"));

    internal static IQueryable<PendingCatalogConnectJob> QueryOrphanedCatalogConnectJobs(DataContext db) =>
        db.pendingCatalogConnectJobs.Where(p =>
            (p.status == CatalogConnectJobStatus.Pending
             || p.status == CatalogConnectJobStatus.InProgress
             || p.status == CatalogConnectJobStatus.Failed)
            && db.operations.Any(o => o.operationId == p.operationId && o.status == "cancelled"));
}
