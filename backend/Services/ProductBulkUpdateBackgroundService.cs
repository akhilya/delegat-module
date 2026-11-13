using Microsoft.EntityFrameworkCore;
using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// Background processor for queued product bulk updates.
/// Pattern mirrors <see cref="ProductTransferBackgroundService"/>.
/// </summary>
public class ProductBulkUpdateBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductBulkUpdateBackgroundService> _logger;
    private readonly IOperationLogger _operationLogger;

    private const int POLL_INTERVAL_MS = 1500;
    private const int ERROR_DELAY_MS = 10_000;
    private const int BATCH_SIZE = 2;
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromHours(2);

    public ProductBulkUpdateBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductBulkUpdateBackgroundService> logger,
        IOperationLogger operationLogger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _operationLogger = operationLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProductBulkUpdateBackgroundService started");

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
                _logger.LogError(ex, "Error in ProductBulkUpdateBackgroundService");
                await Task.Delay(ERROR_DELAY_MS, stoppingToken);
            }
        }

        _logger.LogInformation("ProductBulkUpdateBackgroundService stopped");
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var service = scope.ServiceProvider.GetRequiredService<ProductBulkUpdateService>();

        var staleInProgressBefore = DateTime.UtcNow - InProgressTimeout;

        var pending = await context.pendingProductBulkUpdates
            .Where(t => t.status == ProductBulkUpdateStatus.Pending ||
                        (t.status == ProductBulkUpdateStatus.InProgress &&
                         (t.startedAt ?? t.createdAt) < staleInProgressBefore))
            .OrderBy(t => t.createdAt)
            .Take(BATCH_SIZE)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return;

        foreach (var item in pending)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessSingleAsync(context, service, item, ct);
        }
    }

    private async Task ProcessSingleAsync(
        DataContext context,
        ProductBulkUpdateService service,
        PendingProductBulkUpdate item,
        CancellationToken stoppingToken)
    {
        var operationStatus = await context.operations
            .AsNoTracking()
            .Where(o => o.operationId == item.batchId)
            .Select(o => o.status)
            .FirstOrDefaultAsync(stoppingToken);

        if (OperationLifecycle.IsStopRequested(operationStatus))
        {
            item.status = ProductBulkUpdateStatus.Cancelled;
            item.finishedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(stoppingToken);
            return;
        }

        item.status = ProductBulkUpdateStatus.InProgress;
        item.startedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(stoppingToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var stopWatcher = OperationStopWatcher.WatchAsync(_scopeFactory, item.batchId, cts);

        try
        {
            var productIds = ProductBulkUpdateService.DeserializeProductIds(item.productIdsJson);
            var updates = ProductBulkUpdateService.DeserializeUpdates(item.updatesJson);

            var result = await service.ExecuteAsync(
                item.storeId,
                productIds,
                updates,
                cts.Token,
                _operationLogger,
                item.batchId,
                job: item);

            cts.Cancel();
            try { await stopWatcher; } catch { /* ignored */ }

            item.completedCount = result.updatedCount;
            item.failedCount = result.failedCount;
            item.finishedAt = DateTime.UtcNow;

            string finalStatus;
            string summary;
            if (result.failedCount > 0 && result.updatedCount > 0)
            {
                item.status = ProductBulkUpdateStatus.Partial;
                finalStatus = "partial";
                summary = $"Updated {result.updatedCount}, failed {result.failedCount}";
            }
            else if (result.failedCount > 0)
            {
                item.status = ProductBulkUpdateStatus.Failed;
                finalStatus = "failed";
                summary = $"Failed for all {result.failedCount} product(s)";
                item.lastError = result.errors.FirstOrDefault();
            }
            else
            {
                item.status = ProductBulkUpdateStatus.Completed;
                finalStatus = "completed";
                summary = $"Updated {result.updatedCount} product(s)";
            }

            await context.SaveChangesAsync(stoppingToken);
            await _operationLogger.FinishAsync(item.batchId, item.storeId, finalStatus, summary);
        }
        catch (DbUpdateConcurrencyException)
        {
            cts.Cancel();
            try { await stopWatcher; } catch { /* ignored */ }
            await context.Entry(item).ReloadAsync(stoppingToken);
            _logger.LogInformation("Bulk update {BatchId} checkpoint advanced in another worker", item.batchId);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            item.status = ProductBulkUpdateStatus.Cancelled;
            item.finishedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(CancellationToken.None);
            await _operationLogger.FinishAsync(item.batchId, item.storeId, "cancelled",
                $"Bulk update cancelled after {item.completedCount} product(s)");
            try { await stopWatcher; } catch { /* ignored */ }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            try { await stopWatcher; } catch { /* ignored */ }
            throw;
        }
        catch (Exception ex)
        {
            cts.Cancel();
            try { await stopWatcher; } catch { /* ignored */ }

            _logger.LogError(ex, "Bulk update {BatchId} failed", item.batchId);
            item.status = item.completedCount > 0 ? ProductBulkUpdateStatus.Partial : ProductBulkUpdateStatus.Failed;
            item.lastError = ex.Message;
            item.finishedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(CancellationToken.None);
            await _operationLogger.LogAsync(item.batchId, item.storeId, "error", ex.Message);
            await _operationLogger.FinishAsync(item.batchId, item.storeId,
                item.completedCount > 0 ? "partial" : "failed", ex.Message);
        }
    }
}
