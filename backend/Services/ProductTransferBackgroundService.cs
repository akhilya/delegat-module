using Microsoft.EntityFrameworkCore;
using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// Background processor for queued product transfers (clone/move into another store).
///
/// Pattern mirrors <see cref="ProductImportBackgroundService"/>:
///   - Poll <c>pendingProductTransfers</c> for Pending rows
///   - Reconstruct a TransferRequest from the row, dispatch to ProductTransferService
///   - Surface progress / per-file warnings via OperationLogger (visible in side panel)
///   - Honor cancel: when the operation status flips to "cancelled", we trip the
///     CancellationToken so ExecuteAsync rolls back (cleanup of partial MinIO blobs
///     happens inside ExecuteAsync's catch).
///
/// Failure policy:
///   - Per-file copy failure → skip + warn, final status "partial"
///   - Validation/DB/cancellation failure → status "failed" or "cancelled", no new product
///   - No auto-retry. User can re-enqueue manually if needed.
/// </summary>
public class ProductTransferBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductTransferBackgroundService> _logger;
    private readonly IOperationLogger _operationLogger;

    private const int POLL_INTERVAL_MS = 1500;
    private const int ERROR_DELAY_MS = 10_000;
    private const int BATCH_SIZE = 3; // transfers are heavy - process few at a time
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromHours(6);

    public ProductTransferBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductTransferBackgroundService> logger,
        IOperationLogger operationLogger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _operationLogger = operationLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProductTransferBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
                await Task.Delay(POLL_INTERVAL_MS, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProductTransferBackgroundService");
                await Task.Delay(ERROR_DELAY_MS, stoppingToken);
            }
        }

        _logger.LogInformation("ProductTransferBackgroundService stopped");
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<ProductTransferService>();

        var staleInProgressBefore = DateTime.UtcNow - InProgressTimeout;

        var pending = await context.pendingProductTransfers
            .Where(t => t.status == ProductTransferStatus.Pending ||
                        (t.status == ProductTransferStatus.InProgress &&
                         (t.startedAt ?? t.createdAt) < staleInProgressBefore))
            .OrderBy(t => t.createdAt)
            .Take(BATCH_SIZE)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var item in pending)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessSingleAsync(context, transferService, item, ct);
        }
    }

    private async Task ProcessSingleAsync(
        DataContext context,
        ProductTransferService transferService,
        PendingProductTransfer item,
        CancellationToken stoppingToken)
    {
        // Operation could have been cancelled between enqueue and pickup.
        var operationStatus = await context.operations
            .AsNoTracking()
            .Where(o => o.operationId == item.batchId)
            .Select(o => o.status)
            .FirstOrDefaultAsync(stoppingToken);

        if (operationStatus == "cancelled")
        {
            item.status = ProductTransferStatus.Cancelled;
            item.finishedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(stoppingToken);
            return;
        }

        item.status = ProductTransferStatus.InProgress;
        item.startedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(stoppingToken);

        // CTS that trips when either the host shuts down OR the user clicks "Stop"
        // on the operation in the side panel. We poll the operation status every
        // ~2 seconds in a side task and call cts.Cancel() if it flipped.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var stopWatcher = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(2000, cts.Token);
                    using var checkScope = _scopeFactory.CreateScope();
                    var checkDb = checkScope.ServiceProvider.GetRequiredService<DataContext>();
                    var status = await checkDb.operations.AsNoTracking()
                        .Where(o => o.operationId == item.batchId)
                        .Select(o => o.status)
                        .FirstOrDefaultAsync(cts.Token);
                    if (status == "cancelled" || status == "paused")
                    {
                        cts.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
        }, cts.Token);

        var request = new TransferRequest
        {
            sourceProductId = item.sourceProductId,
            targetStoreId = item.targetStoreId,
            mode = item.mode,
            newSku = item.newSku,
            categoryAction = item.categoryAction,
            categoryId = item.categoryId,
            groupAction = item.groupAction,
            groupId = item.groupId,
            groupName = item.groupName,
            confirmBreakLinks = item.confirmBreakLinks,
            syncMissingCategoryAttributes = item.syncMissingCategoryAttributes,
            copyFiles = item.copyFiles
        };

        try
        {
            var result = await transferService.ExecuteAsync(
                request,
                item.sourceStoreId,
                item.authorUserId,
                cts.Token,
                _operationLogger,
                item.batchId);

            // Stop the watcher
            cts.Cancel();
            try { await stopWatcher; } catch { /* ignored */ }

            // Finalize record + log finish
            item.newProductId = result.newProductId;
            item.finishedAt = DateTime.UtcNow;

            string finalStatus;
            string summary;
            if (result.filesFailed > 0)
            {
                item.status = ProductTransferStatus.Partial;
                finalStatus = "partial";
                summary = $"Done ({item.mode}): files copied {result.filesCopied}, skipped {result.filesFailed}";
            }
            else
            {
                item.status = ProductTransferStatus.Completed;
                finalStatus = "completed";
                summary = item.mode == "move"
                    ? $"Transferred: {result.filesCopied} files, original moved to trash"
                    : $"Cloned: {result.filesCopied} files";
            }

            await context.SaveChangesAsync(stoppingToken);
            await _operationLogger.FinishAsync(item.batchId, item.sourceStoreId, finalStatus, summary);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            // User-initiated cancellation via the operations panel.
            item.status = ProductTransferStatus.Cancelled;
            item.finishedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(CancellationToken.None);
            await _operationLogger.FinishAsync(item.batchId, item.sourceStoreId, "cancelled",
                "Transfer cancelled, changes rolled back");
            try { await stopWatcher; } catch { /* ignored */ }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown - leave record InProgress so it picks up next launch.
            // Don't touch status. Don't call FinishAsync.
            try { await stopWatcher; } catch { /* ignored */ }
            throw;
        }
        catch (Exception ex)
        {
            cts.Cancel();
            try { await stopWatcher; } catch { /* ignored */ }

            item.status = ProductTransferStatus.Failed;
            item.finishedAt = DateTime.UtcNow;
            item.lastError = ex.Message;
            await context.SaveChangesAsync(CancellationToken.None);

            _logger.LogError(ex, "Product {ProductId} transfer failed (operation {OpId})",
                item.sourceProductId, item.batchId);

            await _operationLogger.LogAsync(item.batchId, item.sourceStoreId, "error",
                $"Transfer error: {ex.Message}");
            await _operationLogger.FinishAsync(item.batchId, item.sourceStoreId, "failed", ex.Message);
        }
    }
}
