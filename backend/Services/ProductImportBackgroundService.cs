using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Services.Marketplace;

namespace Delegat.Services;

/// <summary>
/// Background service that processes the pending product import queue.
/// Imports products from marketplaces via the ProductSyncService.
/// </summary>
public class ProductImportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductImportBackgroundService> _logger;
    private readonly IOperationLogger _operationLogger;

    // Settings
    private const int BATCH_SIZE = 10;                // Products to process per batch
    private const int DELAY_BETWEEN_PRODUCTS_MS = 200;   // Delay between products (ms)
    private const int POLL_INTERVAL_MS = 1500;        // Queue poll interval (ms)
    private const int ERROR_DELAY_MS = 10000;         // Delay after error (ms)
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Initializes a new instance of <see cref="ProductImportBackgroundService"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory for creating DI scopes.</param>
    /// <param name="logger">Logger instance.</param>
    public ProductImportBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductImportBackgroundService> logger,
        IOperationLogger operationLogger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _operationLogger = operationLogger;
    }

    /// <summary>
    /// Main execution loop that polls for pending product imports and processes them in batches.
    /// </summary>
    /// <param name="stoppingToken">Token to signal graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProductImportBackgroundService запущен");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingImportsAsync(stoppingToken);
                await Task.Delay(POLL_INTERVAL_MS, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Нормальная остановка
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в ProductImportBackgroundService");
                await Task.Delay(ERROR_DELAY_MS, stoppingToken);
            }
        }

        _logger.LogInformation("ProductImportBackgroundService остановлен");
    }

    private async Task ProcessPendingImportsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var productSyncService = scope.ServiceProvider.GetRequiredService<ProductSyncService>();

        var staleInProgressBefore = DateTime.UtcNow - InProgressTimeout;

        // Получаем пачку pending импортов. Stale InProgress rows are retried after a
        // worker restart/crash; otherwise they would keep the operation running forever.
        var pendingImports = await context.pendingProductImports
            .Where(p => p.status == ProductImportStatus.Pending ||
                       (p.status == ProductImportStatus.Failed && p.retryCount < PendingProductImport.MAX_RETRIES) ||
                       (p.status == ProductImportStatus.InProgress &&
                        (p.lastAttemptAt ?? p.createdAt) < staleInProgressBefore))
            .OrderBy(p => p.createdAt)
            .Take(BATCH_SIZE)
            .ToListAsync(ct);

        if (pendingImports.Count == 0)
            return;

        _logger.LogInformation(
            "Обрабатываю {Count} товаров из очереди импорта: [{Ids}]",
            pendingImports.Count,
            string.Join(", ", pendingImports.Select(p => $"{p.Id}:{p.externalProductId}")));

        foreach (var pending in pendingImports)
        {
            if (ct.IsCancellationRequested)
                break;

            // Check if operation was paused or cancelled
            if (pending.batchId != null)
            {
                var opStatus = await context.operations.AsNoTracking()
                    .Where(o => o.operationId == pending.batchId)
                    .Select(o => o.status)
                    .FirstOrDefaultAsync(ct);

                if (opStatus is "paused" or "cancelled")
                {
                    _logger.LogInformation("Операция {BatchId} в статусе {Status}, пропускаю обработку", pending.batchId, opStatus);
                    break;
                }
            }

            try
            {
                await ProcessSingleImportAsync(context, productSyncService, pending, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка при обработке товара {Id}:{ExternalId}",
                    pending.Id, pending.externalProductId);
            }

            // Задержка между товарами чтобы не перегружать API маркетплейса
            if (!ct.IsCancellationRequested)
                await Task.Delay(DELAY_BETWEEN_PRODUCTS_MS, ct);
        }
    }

    private async Task ProcessSingleImportAsync(
        DataContext context,
        ProductSyncService productSyncService,
        PendingProductImport pending,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Начинаю обработку товара {Id}:{ExternalId} (текущий статус: {Status})",
            pending.Id, pending.externalProductId, pending.status);

        pending.status = ProductImportStatus.InProgress;
        pending.lastAttemptAt = DateTime.UtcNow;
        pending.retryCount++;
        await context.SaveChangesAsync(ct);

        _logger.LogDebug("Статус товара {Id} изменён на InProgress", pending.Id);

        try
        {
            // Импортируем товар (detailed logging happens inside ImportProductAsync)
            var result = await productSyncService.ImportProductAsync(
                pending.connectionId,
                pending.externalProductId,
                pending.targetCategoryId,
                pending.authorUserId,
                ct,
                batchId: pending.batchId,
                batchStoreId: pending.storeId,
                imageBatchId: pending.batchId != null ? $"{pending.batchId}-img" : null);

            _logger.LogInformation(
                "Результат импорта товара {Id}: IsSuccess={IsSuccess}, AlreadyLinked={AlreadyLinked}, Error={Error}",
                pending.Id, result.IsSuccess, result.AlreadyLinked, result.ErrorMessage);

            if (result.IsSuccess || result.AlreadyLinked)
            {
                pending.status = ProductImportStatus.Completed;
                pending.completedAt = DateTime.UtcNow;
                pending.importedProductId = result.ProductId;
                pending.productName = result.ProductName;
                pending.lastError = null;

                if (result.AlreadyLinked)
                {
                    _logger.LogInformation(
                        "Товар {ExternalId} уже был импортирован ранее (Product ID: {ProductId})",
                        pending.externalProductId, result.ProductId);
                }
                else
                {
                    _logger.LogInformation(
                        "Импортирован товар {ProductName} (ID: {ProductId}) из очереди",
                        result.ProductName, result.ProductId);
                }

                if (pending.batchId != null)
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId,
                        result.AlreadyLinked ? "warning" : "success",
                        result.AlreadyLinked
                            ? $"Уже импортирован: {pending.productName ?? pending.externalProductId}"
                            : $"Импортирован: {result.ProductName ?? pending.externalProductId} (ID: {result.ProductId})");
            }
            else
            {
                pending.status = ProductImportStatus.Failed;
                pending.lastError = result.ErrorMessage ?? "Неизвестная ошибка";

                _logger.LogWarning(
                    "Не удалось импортировать товар {ExternalId}: {Error} (попытка {Retry})",
                    pending.externalProductId, pending.lastError, pending.retryCount);

                if (pending.batchId != null)
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId, "error",
                        $"Ошибка: {pending.productName ?? pending.externalProductId} — {pending.lastError}");
            }

            await context.SaveChangesAsync(ct);

            // Emit progress via SignalR
            if (pending.batchId != null)
            {
                var batchItems = await context.pendingProductImports
                    .AsNoTracking()
                    .Where(p => p.batchId == pending.batchId)
                    .ToListAsync(ct);
                var total = batchItems.Count;
                var completed = batchItems.Count(p => p.status == ProductImportStatus.Completed);
                var failed = batchItems.Count(p => p.status == ProductImportStatus.Failed);
                await _operationLogger.ProgressAsync(pending.batchId, pending.storeId, total, completed, failed);

                if (total == completed + failed)
                    await _operationLogger.FinishAsync(pending.batchId, pending.storeId,
                        failed == 0 ? "completed" : (completed == 0 ? "failed" : "partial"),
                        $"Импортировано: {completed} успешно, {failed} ошибок");
            }

            _logger.LogInformation(
                "Товар {Id}:{ExternalId} обработан, финальный статус: {Status}",
                pending.Id, pending.externalProductId, pending.status);
        }
        catch (Exception ex)
        {
            pending.status = ProductImportStatus.Failed;
            pending.lastError = ex.Message;
            await context.SaveChangesAsync(CancellationToken.None); // Сохраняем даже если отмена

            _logger.LogError(ex,
                "Ошибка импорта товара {Id}:{ExternalId} (попытка {Retry})",
                pending.Id, pending.externalProductId, pending.retryCount);
        }
    }
}
