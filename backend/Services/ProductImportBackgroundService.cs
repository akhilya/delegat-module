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
    /// <param name="operationLogger">Operation progress logger.</param>
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
        _logger.LogInformation("ProductImportBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingImportsAsync(stoppingToken);
                await Task.Delay(POLL_INTERVAL_MS, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProductImportBackgroundService");
                await Task.Delay(ERROR_DELAY_MS, stoppingToken);
            }
        }

        _logger.LogInformation("ProductImportBackgroundService stopped");
    }

    internal async Task ProcessPendingImportsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();

        var staleInProgressBefore = DateTime.UtcNow - InProgressTimeout;

        var candidates = await context.pendingProductImports
            .AsNoTracking()
            .Where(p => p.status == ProductImportStatus.Pending ||
                       (p.status == ProductImportStatus.Failed && p.retryCount < PendingProductImport.MAX_RETRIES) ||
                       (p.status == ProductImportStatus.InProgress &&
                        (p.lastAttemptAt ?? p.createdAt) < staleInProgressBefore))
            .OrderBy(p => p.createdAt)
            .Take(100)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        var connIds = candidates.Select(p => p.connectionId).Distinct().ToList();
        var extIds = candidates
            .Select(p => p.externalCategoryId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        var mappingByKey = new Dictionary<(int connectionId, string externalCategoryId), int>();
        var failedCategoryByKey = new Dictionary<(int connectionId, string externalCategoryId), string>();

        if (extIds.Count > 0)
        {
            var mappingKeys = await context.categoryMappings.AsNoTracking()
                .Where(m => connIds.Contains(m.connectionId)
                            && extIds.Contains(m.externalCategoryId))
                .Select(m => new { m.connectionId, m.externalCategoryId, m.localCategoryId })
                .ToListAsync(ct);
            mappingByKey = mappingKeys
                .GroupBy(m => (m.connectionId, m.externalCategoryId))
                .ToDictionary(g => g.Key, g => g.First().localCategoryId);

            var categoryJobs = await context.pendingCategoryImports.AsNoTracking()
                .Where(j => connIds.Contains(j.connectionId)
                            && extIds.Contains(j.externalCategoryId)
                            && j.status != CategoryImportStatus.Cancelled
                            && j.status != CategoryImportStatus.Completed)
                .ToListAsync(ct);
            failedCategoryByKey = categoryJobs
                .Where(j => j.status == CategoryImportStatus.Failed
                            && j.retryCount >= PendingCategoryImport.MAX_RETRIES)
                .GroupBy(j => (j.connectionId, j.externalCategoryId))
                .ToDictionary(g => g.Key, g => g.First().lastError ?? "Category import failed");
        }

        var pendingImports = new List<PendingProductImport>();
        foreach (var p in candidates)
        {
            if (pendingImports.Count >= BATCH_SIZE)
                break;
            if (IsProductImportReady(p, mappingByKey, failedCategoryByKey))
                pendingImports.Add(p);
        }

        if (pendingImports.Count == 0)
            return;

        _logger.LogInformation(
            "Processing {Count} products from the import queue: [{Ids}]",
            pendingImports.Count,
            string.Join(", ", pendingImports.Select(p => $"{p.Id}:{p.externalProductId}")));

        foreach (var pending in pendingImports)
        {
            if (ct.IsCancellationRequested)
                break;

            if (pending.batchId != null
                && await OperationLifecycle.IsStopRequestedAsync(context, pending.batchId, ct))
            {
                _logger.LogInformation("Operation {BatchId} was stopped, cancelling queue rows", pending.batchId);
                await context.pendingProductImports
                    .Where(p => p.batchId == pending.batchId &&
                        (p.status == ProductImportStatus.Pending
                         || p.status == ProductImportStatus.InProgress
                         || p.status == ProductImportStatus.Failed))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductImportStatus.Cancelled), ct);
                await _operationLogger.FinishAsync(pending.batchId, pending.storeId, "cancelled", "Stopped");
                break;
            }

            try
            {
                // A separate scope bounds EF tracking to one product, including large
                // category dictionaries, and discards failed tracked writes before retry.
                using var itemScope = _scopeFactory.CreateScope();
                var itemContext = itemScope.ServiceProvider.GetRequiredService<DataContext>();
                var productSyncService = itemScope.ServiceProvider.GetRequiredService<ProductSyncService>();
                var trackedPending = await itemContext.pendingProductImports
                    .SingleOrDefaultAsync(p => p.Id == pending.Id, ct);
                if (trackedPending == null || trackedPending.status == ProductImportStatus.Cancelled)
                    continue;
                var quotaExhausted = await ProcessSingleImportAsync(
                    itemContext, productSyncService, trackedPending, mappingByKey, failedCategoryByKey, ct);
                if (quotaExhausted)
                    break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error while processing product {Id}:{ExternalId}",
                    pending.Id, pending.externalProductId);
            }

            if (!ct.IsCancellationRequested
                && (pending.batchId == null
                    || !await OperationLifecycle.IsStopRequestedAsync(context, pending.batchId, ct)))
                await Task.Delay(DELAY_BETWEEN_PRODUCTS_MS, ct);
        }
    }

    internal static bool IsProductImportReady(
        PendingProductImport pending,
        Dictionary<(int connectionId, string externalCategoryId), int> mappingByKey,
        Dictionary<(int connectionId, string externalCategoryId), string> failedCategoryByKey)
    {
        if (pending.forceImportWithoutMapping)
            return true;
        if (pending.targetCategoryId != null)
            return true;
        if (string.IsNullOrWhiteSpace(pending.externalCategoryId))
            return true;
        var key = (pending.connectionId, pending.externalCategoryId);
        if (mappingByKey.ContainsKey(key))
            return true;
        if (failedCategoryByKey.ContainsKey(key))
            return true;
        return false;
    }

    /// <returns>True when MAX_PRODUCTS was hit and the rest of the batch was failed.</returns>
    private async Task<bool> ProcessSingleImportAsync(
        DataContext context,
        ProductSyncService productSyncService,
        PendingProductImport pending,
        Dictionary<(int connectionId, string externalCategoryId), int> mappingByKey,
        Dictionary<(int connectionId, string externalCategoryId), string> failedCategoryByKey,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Starting to process product {Id}:{ExternalId} (current status: {Status})",
            pending.Id, pending.externalProductId, pending.status);

        pending.status = ProductImportStatus.InProgress;
        pending.lastAttemptAt = DateTime.UtcNow;
        pending.retryCount++;
        await context.SaveChangesAsync(ct);

        if (pending.targetCategoryId == null
            && !string.IsNullOrWhiteSpace(pending.externalCategoryId)
            && !pending.forceImportWithoutMapping)
        {
            var key = (pending.connectionId, pending.externalCategoryId);
            if (failedCategoryByKey.TryGetValue(key, out var catError))
            {
                pending.status = ProductImportStatus.Failed;
                pending.lastError = catError;
                await context.SaveChangesAsync(ct);
                if (pending.batchId != null)
                {
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId, "error",
                        $"Skipped: {pending.externalProductId} - {catError}");
                    await EmitBatchProgressAsync(context, pending, ct);
                }
                return false;
            }

            if (mappingByKey.TryGetValue(key, out var localCategoryId))
                pending.targetCategoryId = localCategoryId;
        }

        _logger.LogDebug("Product {Id} status changed to InProgress", pending.Id);

        try
        {
            // Import the product (detailed logging happens inside ImportProductAsync)
            var result = await productSyncService.ImportProductAsync(
                pending.connectionId,
                pending.externalProductId,
                pending.targetCategoryId,
                pending.authorUserId,
                ct,
                batchId: pending.batchId,
                batchStoreId: pending.storeId,
                imageBatchId: pending.batchId != null ? $"{pending.batchId}-img" : null);

            _logger.LogDebug(
                "Product import result {Id}: IsSuccess={IsSuccess}, AlreadyLinked={AlreadyLinked}, Error={Error}",
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
                    _logger.LogDebug(
                        "Product {ExternalId} was already imported previously (Product ID: {ProductId})",
                        pending.externalProductId, result.ProductId);
                }
                else
                {
                    _logger.LogDebug(
                        "Imported product {ProductName} (ID: {ProductId}) from the queue",
                        result.ProductName, result.ProductId);
                }

                if (pending.batchId != null)
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId,
                        result.AlreadyLinked ? "warning" : "success",
                        result.AlreadyLinked
                            ? result.ProductId is int alreadyId
                                ? $"Already imported: {OperationProductRef.Format(alreadyId, pending.productName)}"
                                : $"Already imported: {pending.productName ?? pending.externalProductId}"
                            : result.ProductId is int importedId
                                ? $"Imported: {OperationProductRef.Format(importedId, result.ProductName ?? pending.productName)}"
                                : $"Imported: {result.ProductName ?? pending.externalProductId}");
            }
            else
            {
                pending.status = ProductImportStatus.Failed;
                pending.lastError = result.ErrorMessage ?? "Unknown error";
                var quotaHit = result.ErrorCode == "product_limit_reached";
                if (quotaHit)
                    pending.retryCount = PendingProductImport.MAX_RETRIES;

                _logger.LogWarning(
                    "Failed to import product {ExternalId}: {Error} (attempt {Retry})",
                    pending.externalProductId, pending.lastError, pending.retryCount);

                if (pending.batchId != null && !quotaHit)
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId, "error",
                        $"Error: {pending.productName ?? pending.externalProductId} - {pending.lastError}");

                await context.SaveChangesAsync(ct);

                if (quotaHit)
                {
                    var skipped = await FailRemainingForQuotaAsync(context, pending, ct);
                    if (pending.batchId != null)
                    {
                        var msg = skipped > 0
                            ? $"Product limit reached. Skipped the remaining {skipped} products."
                            : pending.lastError ?? "Product limit reached";
                        await _operationLogger.LogAsync(pending.batchId, pending.storeId, "error", msg);
                    }
                }

                if (pending.batchId != null)
                    await EmitBatchProgressAsync(context, pending, ct);

                _logger.LogInformation(
                    "Product {Id}:{ExternalId} processed, final status: {Status}",
                    pending.Id, pending.externalProductId, pending.status);

                return quotaHit;
            }

            await context.SaveChangesAsync(ct);

            if (pending.batchId != null)
                await EmitBatchProgressAsync(context, pending, ct);

            _logger.LogInformation(
                "Product {Id}:{ExternalId} processed, final status: {Status}",
                pending.Id, pending.externalProductId, pending.status);

            return false;
        }
        catch (Exception ex)
        {
            pending.status = ProductImportStatus.Failed;
            pending.lastError = ex.Message;
            await context.SaveChangesAsync(CancellationToken.None); // Save even if cancelled

            _logger.LogError(ex,
                "Error importing product {Id}:{ExternalId} (attempt {Retry})",
                pending.Id, pending.externalProductId, pending.retryCount);

            return false;
        }
    }

    private async Task<int> FailRemainingForQuotaAsync(
        DataContext context,
        PendingProductImport hit,
        CancellationToken ct)
    {
        var error = hit.lastError ?? "Product limit reached";
        IQueryable<PendingProductImport> query = context.pendingProductImports;
        if (!string.IsNullOrEmpty(hit.batchId))
            query = query.Where(p => p.batchId == hit.batchId);
        else
            query = query.Where(p => p.storeId == hit.storeId && p.batchId == null);

        var rows = await query.ToListAsync(ct);
        var remaining = ProductImportQuotaStop.RemainingForQuota(rows, hit).ToList();
        var skipped = ProductImportQuotaStop.MarkRemainingFailed(remaining, hit.Id, error);
        if (skipped > 0)
            await context.SaveChangesAsync(ct);
        return skipped;
    }

    private async Task EmitBatchProgressAsync(
        DataContext context,
        PendingProductImport pending,
        CancellationToken ct)
    {
        if (pending.batchId == null)
            return;

        var counts = await context.pendingProductImports
            .AsNoTracking()
            .Where(p => p.batchId == pending.batchId)
            .GroupBy(p => p.status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(g => g.Count);
        var completed = counts.Where(g => g.Status == ProductImportStatus.Completed).Sum(g => g.Count);
        var failed = counts.Where(g => g.Status == ProductImportStatus.Failed).Sum(g => g.Count);
        await _operationLogger.ProgressAsync(pending.batchId, pending.storeId, total, completed, failed);

        if (total != completed + failed)
            return;

        await _operationLogger.FinishAsync(pending.batchId, pending.storeId,
            failed == 0 ? "completed" : (completed == 0 ? "failed" : "partial"),
            $"Imported: {completed} succeeded, {failed} failed");

        await ImageDownloadBackgroundService.FinishIdleImageDownloadOpsAsync(
            context,
            _operationLogger,
            ct,
            onlyBatchId: $"{pending.batchId}-img");
    }
}
