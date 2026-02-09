using Microsoft.EntityFrameworkCore;
using PrintO.Models;
using PrintO.Services.Marketplace;

namespace PrintO.Services;

/// <summary>
/// Background service that processes the pending product export queue.
/// Exports products to marketplaces via the ProductSyncService.
/// </summary>
public class ProductExportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductExportBackgroundService> _logger;

    // Settings
    private const int BATCH_SIZE = 5;                 // Products to process per batch
    private const int DELAY_BETWEEN_PRODUCTS_MS = 300;   // Delay between products (ms)
    private const int POLL_INTERVAL_MS = 1000;        // Queue poll interval (ms)
    private const int ERROR_DELAY_MS = 5000;          // Delay after error (ms)

    /// <summary>
    /// Initializes a new instance of <see cref="ProductExportBackgroundService"/>.
    /// </summary>
    /// <param name="scopeFactory">Factory for creating DI scopes.</param>
    /// <param name="logger">Logger instance.</param>
    public ProductExportBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductExportBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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

        // Получаем пачку pending экспортов
        var pendingExports = await context.pendingProductExports
            .Where(p => p.status == ProductExportStatus.Pending ||
                       (p.status == ProductExportStatus.Failed && p.retryCount < PendingProductExport.MAX_RETRIES))
            .OrderBy(p => p.createdAt)
            .Take(BATCH_SIZE)
            .ToListAsync(ct);

        if (pendingExports.Count == 0)
            return;

        _logger.LogInformation(
            "Обрабатываю {Count} товаров из очереди экспорта: [{Ids}]",
            pendingExports.Count,
            string.Join(", ", pendingExports.Select(p => $"{p.Id}:{p.productId}")));

        foreach (var pending in pendingExports)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                await ProcessSingleExportAsync(context, productSyncService, pending, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка при обработке экспорта товара {Id}:{ProductId}",
                    pending.Id, pending.productId);
            }

            // Задержка между товарами чтобы не перегружать API маркетплейса
            if (!ct.IsCancellationRequested)
                await Task.Delay(DELAY_BETWEEN_PRODUCTS_MS, ct);
        }
    }

    private async Task ProcessSingleExportAsync(
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
            }
            else
            {
                pending.status = ProductExportStatus.Failed;
                pending.lastError = result.ErrorMessage ?? "Неизвестная ошибка";

                _logger.LogWarning(
                    "Не удалось экспортировать товар {ProductId}: {Error} (попытка {Retry})",
                    pending.productId, pending.lastError, pending.retryCount);
            }

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Товар {Id}:{ProductId} обработан, финальный статус: {Status}",
                pending.Id, pending.productId, pending.status);
        }
        catch (Exception ex)
        {
            pending.status = ProductExportStatus.Failed;
            pending.lastError = ex.Message;
            await context.SaveChangesAsync(CancellationToken.None); // Сохраняем даже если отмена

            _logger.LogError(ex,
                "Ошибка экспорта товара {Id}:{ProductId} (попытка {Retry})",
                pending.Id, pending.productId, pending.retryCount);
        }
    }
}
