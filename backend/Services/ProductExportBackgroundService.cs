using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Services.Marketplace;

namespace Delegat.Services;

/// <summary>
/// Background service that processes the pending product export queue.
/// Exports products to marketplaces via the ProductSyncService.
/// Uses bulk export when the adapter supports it.
/// </summary>
public class ProductExportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductExportBackgroundService> _logger;
    private readonly IOperationLogger _operationLogger;

    // Settings
    private const int BATCH_SIZE = 50;                // Products to process per batch
    private const int POLL_INTERVAL_MS = 1000;        // Queue poll interval (ms)
    private const int ERROR_DELAY_MS = 5000;          // Delay after error (ms)
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Initializes a new instance of <see cref="ProductExportBackgroundService"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory for creating DI scopes.</param>
    /// <param name="logger">Logger instance.</param>
    public ProductExportBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductExportBackgroundService> logger,
        IOperationLogger operationLogger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _operationLogger = operationLogger;
    }

    /// <summary>
    /// Main execution loop that polls for pending product exports and processes them in batches.
    /// </summary>
    /// <param name="stoppingToken">Token to signal graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProductExportBackgroundService запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingExportsAsync(stoppingToken);
                await Task.Delay(POLL_INTERVAL_MS, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Нормальная остановка
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в ProductExportBackgroundService");
                await Task.Delay(ERROR_DELAY_MS, stoppingToken);
            }
        }

        _logger.LogInformation("ProductExportBackgroundService остановлен");
    }

    private async Task ProcessPendingExportsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var productSyncService = scope.ServiceProvider.GetRequiredService<ProductSyncService>();
        var adapterRegistry = scope.ServiceProvider.GetRequiredService<IMarketplaceAdapterRegistry>();

        var staleInProgressBefore = DateTime.UtcNow - InProgressTimeout;

        // Получаем пачку pending экспортов. Stale InProgress rows are retried after a
        // worker restart/crash; otherwise they would keep the operation running forever.
        var pendingExports = await context.pendingProductExports
            .Where(p => p.status == ProductExportStatus.Pending ||
                       (p.status == ProductExportStatus.Failed && p.retryCount < PendingProductExport.MAX_RETRIES) ||
                       (p.status == ProductExportStatus.InProgress &&
                        (p.lastAttemptAt ?? p.createdAt) < staleInProgressBefore))
            .OrderBy(p => p.createdAt)
            .Take(BATCH_SIZE)
            .ToListAsync(ct);

        if (pendingExports.Count == 0)
            return;

        _logger.LogInformation(
            "Обрабатываю {Count} товаров из очереди экспорта: [{Ids}]",
            pendingExports.Count,
            string.Join(", ", pendingExports.Select(p => $"{p.Id}:{p.productId}")));

        // Group pending items by connectionId
        var grouped = pendingExports.GroupBy(p => p.connectionId).ToList();

        foreach (var group in grouped)
        {
            if (ct.IsCancellationRequested)
                break;

            var connectionId = group.Key;
            var groupItems = group.ToList();

            // Check if any operation in this group is paused or cancelled
            var batchId = groupItems.FirstOrDefault(i => i.batchId != null)?.batchId;
            if (batchId != null)
            {
                var opStatus = await context.operations.AsNoTracking()
                    .Where(o => o.operationId == batchId)
                    .Select(o => o.status)
                    .FirstOrDefaultAsync(ct);

                if (opStatus is "paused" or "cancelled")
                {
                    _logger.LogInformation("Операция {BatchId} в статусе {Status}, пропускаю экспорт", batchId, opStatus);
                    continue;
                }
            }

            // Check if adapter supports bulk export
            var connection = await context.marketplaceConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

            var adapter = connection != null ? adapterRegistry.GetByKey(connection.adapterKey) : null;
            var supportsBulk = adapter?.Capabilities.HasFlag(MarketplaceCapabilities.ExportProductBulk) == true;

            if (supportsBulk && groupItems.Count > 1)
            {
                // Bulk export path
                await ProcessBulkExportAsync(context, productSyncService, groupItems, connectionId, ct);
            }
            else
            {
                // Sequential fallback for non-bulk adapters or single items
                foreach (var pending in groupItems)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    bool isPermanent;
                    try
                    {
                        isPermanent = await ProcessSingleExportAsync(context, productSyncService, pending, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Критическая ошибка при обработке экспорта товара {Id}:{ProductId}",
                            pending.Id, pending.productId);
                        isPermanent = IsPermanentError(ex.Message);
                    }

                    if (isPermanent)
                    {
                        await CancelRemainingExportsAsync(context, pending, ct);
                        break;
                    }
                }
            }
        }
    }

    private async Task ProcessBulkExportAsync(
        DataContext context,
        ProductSyncService productSyncService,
        List<PendingProductExport> pendingItems,
        int connectionId,
        CancellationToken ct)
    {
        // Mark all as InProgress
        foreach (var pending in pendingItems)
        {
            pending.status = ProductExportStatus.InProgress;
            pending.lastAttemptAt = DateTime.UtcNow;
            pending.retryCount++;
        }
        await context.SaveChangesAsync(ct);

        var productIds = pendingItems.Select(p => p.productId).ToList();
        var pendingByProductId = pendingItems.ToDictionary(p => p.productId, p => p);

        _logger.LogInformation(
            "Запуск bulk экспорта {Count} товаров для connectionId={ConnectionId}",
            productIds.Count, connectionId);

        try
        {
            var result = await productSyncService.ExportProductsBulkAsync(productIds, connectionId, ct);

            foreach (var itemResult in result.Items)
            {
                if (!pendingByProductId.TryGetValue(itemResult.ProductId, out var pending))
                    continue;

                if (itemResult.IsSuccess)
                {
                    pending.status = ProductExportStatus.Completed;
                    pending.completedAt = DateTime.UtcNow;
                    pending.externalProductId = itemResult.ExternalProductId;
                    pending.lastError = null;

                    _logger.LogInformation(
                        "Bulk: экспортирован товар {ProductId} -> External={ExternalId}",
                        pending.productId, itemResult.ExternalProductId);

                    if (pending.batchId != null)
                        await _operationLogger.LogAsync(pending.batchId, pending.storeId, "success",
                            $"Экспортирован: {pending.productName ?? pending.productId.ToString()}");
                }
                else
                {
                    pending.status = ProductExportStatus.Failed;
                    pending.lastError = itemResult.ErrorMessage ?? "Неизвестная ошибка";

                    if (IsPermanentError(pending.lastError))
                    {
                        pending.retryCount = PendingProductExport.MAX_RETRIES;
                        await CancelRemainingExportsAsync(context, pending, ct);
                    }

                    _logger.LogWarning(
                        "Bulk: не удалось экспортировать товар {ProductId}: {Error}",
                        pending.productId, pending.lastError);

                    if (pending.batchId != null)
                        await _operationLogger.LogAsync(pending.batchId, pending.storeId, "error",
                            $"Ошибка: {pending.productName ?? pending.productId.ToString()} — {pending.lastError}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk export failed for connectionId={ConnectionId}", connectionId);

            var isPermanent = IsPermanentError(ex.Message);
            foreach (var pending in pendingItems.Where(p => p.status == ProductExportStatus.InProgress))
            {
                pending.status = ProductExportStatus.Failed;
                pending.lastError = ex.Message;
                if (isPermanent)
                    pending.retryCount = PendingProductExport.MAX_RETRIES;
            }

            if (isPermanent)
                await CancelRemainingExportsAsync(context, pendingItems.First(), ct);
        }

        await context.SaveChangesAsync(ct);

        // Emit progress/finish via SignalR for bulk export
        var firstItem = pendingItems.First();
        if (firstItem.batchId != null)
        {
            var batchItems = await context.pendingProductExports
                .AsNoTracking()
                .Where(p => p.batchId == firstItem.batchId)
                .ToListAsync(ct);
            var total = batchItems.Count;
            var completed = batchItems.Count(p => p.status == ProductExportStatus.Completed);
            var failed = batchItems.Count(p => p.status == ProductExportStatus.Failed);
            await _operationLogger.ProgressAsync(firstItem.batchId, firstItem.storeId, total, completed, failed);

            var pending = total - completed - failed;
            if (pending == 0)
                await _operationLogger.FinishAsync(firstItem.batchId, firstItem.storeId,
                    failed == 0 ? "completed" : (completed == 0 ? "failed" : "partial"),
                    $"Экспортировано: {completed} успешно, {failed} ошибок");
        }
    }

    private async Task CancelRemainingExportsAsync(
        DataContext context,
        PendingProductExport triggerItem,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Обнаружена постоянная ошибка для подключения {ConnectionId}, отменяю оставшиеся экспорты",
            triggerItem.connectionId);

        var remaining = await context.pendingProductExports
            .Where(p => p.connectionId == triggerItem.connectionId
                && p.Id != triggerItem.Id
                && (p.status == ProductExportStatus.Pending || p.status == ProductExportStatus.InProgress))
            .ToListAsync(ct);

        foreach (var r in remaining)
        {
            r.status = ProductExportStatus.Failed;
            r.lastError = triggerItem.lastError;
            r.retryCount = PendingProductExport.MAX_RETRIES;
        }

        if (remaining.Count > 0)
        {
            await context.SaveChangesAsync(ct);
            _logger.LogWarning("Отменено {Count} оставшихся экспортов для подключения {ConnectionId}",
                remaining.Count, triggerItem.connectionId);
        }
    }

    /// <summary>
    /// Returns true if the error is permanent (account-level) and retrying won't help.
    /// </summary>
    private static bool IsPermanentError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return false;
        return errorMessage.Contains("исчерпали лимит", StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>true if a permanent (non-retryable) error was encountered.</returns>
    private async Task<bool> ProcessSingleExportAsync(
        DataContext context,
        ProductSyncService productSyncService,
        PendingProductExport pending,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Начинаю экспорт товара {Id}:{ProductId} (текущий статус: {Status})",
            pending.Id, pending.productId, pending.status);

        pending.status = ProductExportStatus.InProgress;
        pending.lastAttemptAt = DateTime.UtcNow;
        pending.retryCount++;
        await context.SaveChangesAsync(ct);

        _logger.LogDebug("Статус экспорта {Id} изменён на InProgress", pending.Id);

        try
        {
            // Экспортируем товар
            var result = await productSyncService.ExportProductAsync(
                pending.productId,
                pending.connectionId,
                ct);

            _logger.LogInformation(
                "Результат экспорта товара {Id}: IsSuccess={IsSuccess}, Error={Error}",
                pending.Id, result.IsSuccess, result.ErrorMessage);

            if (result.IsSuccess)
            {
                pending.status = ProductExportStatus.Completed;
                pending.completedAt = DateTime.UtcNow;
                pending.externalProductId = result.ExternalProductId;
                pending.linkId = result.LinkId;
                pending.lastError = null;

                _logger.LogInformation(
                    "Экспортирован товар {ProductName} (ID: {ProductId}, External: {ExternalId}) из очереди",
                    pending.productName, pending.productId, result.ExternalProductId);

                if (pending.batchId != null)
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId, "success",
                        $"Экспортирован: {pending.productName ?? pending.productId.ToString()}");
            }
            else
            {
                pending.status = ProductExportStatus.Failed;
                pending.lastError = result.ErrorMessage ?? "Неизвестная ошибка";

                if (IsPermanentError(pending.lastError))
                    pending.retryCount = PendingProductExport.MAX_RETRIES;

                _logger.LogWarning(
                    "Не удалось экспортировать товар {ProductId}: {Error} (попытка {Retry})",
                    pending.productId, pending.lastError, pending.retryCount);

                if (pending.batchId != null)
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId, "error",
                        $"Ошибка: {pending.productName ?? pending.productId.ToString()} — {pending.lastError}");
            }

            await context.SaveChangesAsync(ct);

            // Emit progress via SignalR
            if (pending.batchId != null)
            {
                var batchItems = await context.pendingProductExports
                    .AsNoTracking()
                    .Where(p => p.batchId == pending.batchId)
                    .ToListAsync(ct);
                var total = batchItems.Count;
                var completed = batchItems.Count(p => p.status == ProductExportStatus.Completed);
                var failed = batchItems.Count(p => p.status == ProductExportStatus.Failed);
                await _operationLogger.ProgressAsync(pending.batchId, pending.storeId, total, completed, failed);

                if (total == completed + failed)
                    await _operationLogger.FinishAsync(pending.batchId, pending.storeId,
                        failed == 0 ? "completed" : (completed == 0 ? "failed" : "partial"),
                        $"Экспортировано: {completed} успешно, {failed} ошибок");
            }

            _logger.LogInformation(
                "Товар {Id}:{ProductId} обработан, финальный статус: {Status}",
                pending.Id, pending.productId, pending.status);

            return IsPermanentError(pending.lastError);
        }
        catch (Exception ex)
        {
            pending.status = ProductExportStatus.Failed;
            pending.lastError = ex.Message;

            var permanent = IsPermanentError(ex.Message);
            if (permanent)
                pending.retryCount = PendingProductExport.MAX_RETRIES;

            await context.SaveChangesAsync(CancellationToken.None); // Сохраняем даже если отмена

            _logger.LogError(ex,
                "Ошибка экспорта товара {Id}:{ProductId} (попытка {Retry})",
                pending.Id, pending.productId, pending.retryCount);

            return permanent;
        }
    }
}
