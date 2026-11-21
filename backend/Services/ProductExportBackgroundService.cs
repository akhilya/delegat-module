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
    /// <param name="operationLogger">Operation progress logger.</param>
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
        _logger.LogInformation("ProductExportBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingExportsAsync(stoppingToken);
                await Task.Delay(POLL_INTERVAL_MS, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProductExportBackgroundService");
                await Task.Delay(ERROR_DELAY_MS, stoppingToken);
            }
        }

        _logger.LogInformation("ProductExportBackgroundService stopped");
    }

    internal async Task ProcessPendingExportsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var productSyncService = scope.ServiceProvider.GetRequiredService<ProductSyncService>();
        var adapterRegistry = scope.ServiceProvider.GetRequiredService<IMarketplaceAdapterRegistry>();

        var staleInProgressBefore = DateTime.UtcNow - InProgressTimeout;

        // Fetch a batch of pending exports. Stale InProgress rows are retried after a
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
            "Processing {Count} products from the export queue: [{Ids}]",
            pendingExports.Count,
            string.Join(", ", pendingExports.Select(p => $"{p.Id}:{p.productId}")));

        // Rows from different operations must not share one adapter call or one
        // finalization path. Unbatched rows are an operation of their own.
        var grouped = pendingExports
            .GroupBy(p => new { p.connectionId, BatchKey = p.batchId ?? $"queue:{p.Id}" })
            .ToList();

        foreach (var group in grouped)
        {
            if (ct.IsCancellationRequested)
                break;

            var connectionId = group.Key.connectionId;
            var groupItems = group
                .Where(p => p.status == ProductExportStatus.Pending
                    || p.status == ProductExportStatus.InProgress
                    || (p.status == ProductExportStatus.Failed && p.retryCount < PendingProductExport.MAX_RETRIES))
                .ToList();
            if (groupItems.Count == 0)
                continue;

            var batchId = groupItems.FirstOrDefault(i => i.batchId != null)?.batchId;
            if (batchId != null
                && await OperationLifecycle.IsStopRequestedAsync(context, batchId, ct))
            {
                _logger.LogInformation("Operation {BatchId} was stopped, cancelling queue rows", batchId);
                await context.pendingProductExports
                    .Where(p => p.batchId == batchId &&
                        (p.status == ProductExportStatus.Pending
                         || p.status == ProductExportStatus.InProgress
                         || p.status == ProductExportStatus.Failed))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Cancelled), ct);
                var storeId = groupItems.First().storeId;
                await _operationLogger.FinishAsync(batchId, storeId, "cancelled", "Stopped");
                continue;
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
                    if (pending.batchId != null
                        && await OperationLifecycle.IsStopRequestedAsync(context, pending.batchId, ct))
                    {
                        await context.pendingProductExports
                            .Where(p => p.batchId == pending.batchId &&
                                (p.status == ProductExportStatus.Pending
                                 || p.status == ProductExportStatus.InProgress
                                 || p.status == ProductExportStatus.Failed))
                            .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Cancelled), ct);
                        break;
                    }

                    bool isPermanent;
                    try
                    {
                        isPermanent = await ProcessSingleExportAsync(context, productSyncService, pending, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Critical error while processing export for product {Id}:{ProductId}",
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

        var productIds = pendingItems.Select(p => p.productId).Distinct().ToList();
        var pendingByProductId = pendingItems
            .GroupBy(p => p.productId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var firstItem = pendingItems.First();

        _logger.LogInformation(
            "Starting bulk export of {Count} products for connectionId={ConnectionId}",
            productIds.Count, connectionId);

        // Surface activity immediately — otherwise UI stays at 0/pending for the whole
        // adapter HTTP call (Woo products/bulk alone can take minutes).
        if (firstItem.batchId != null)
        {
            await _operationLogger.LogAsync(firstItem.batchId, firstItem.storeId, "info",
                $"Batch mode confirmed: all {pendingItems.Count} products are in progress and are being sent in one adapter call. " +
                "Item counters update when the marketplace returns the batch result.");

            var snapshot = await context.pendingProductExports
                .AsNoTracking()
                .Where(p => p.batchId == firstItem.batchId)
                .Select(p => p.status)
                .ToListAsync(ct);
            var total = snapshot.Count;
            var completed = snapshot.Count(s => s == ProductExportStatus.Completed);
            var failed = snapshot.Count(s => s == ProductExportStatus.Failed);
            await _operationLogger.ProgressAsync(firstItem.batchId, firstItem.storeId, total, completed, failed);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watcher = firstItem.batchId != null
            ? OperationStopWatcher.WatchAsync(_scopeFactory, firstItem.batchId, linked)
            : Task.CompletedTask;
        try
        {
            Func<string, CancellationToken, Task>? progress = firstItem.batchId == null
                ? null
                : (message, _) => _operationLogger.LogAsync(
                    firstItem.batchId, firstItem.storeId, "info", message);

            var bulkRequestKey = $"pending-export-batch:{firstItem.batchId ?? "queue"}:{string.Join(',', pendingItems.OrderBy(p => p.Id).Select(p => p.Id))}";
            var result = await productSyncService.ExportProductsBulkAsync(
                productIds, connectionId, linked.Token, progress, requestKey: bulkRequestKey);

            if (result.IsPending && !string.IsNullOrWhiteSpace(result.IntegrationCommandId))
            {
                if (firstItem.batchId != null)
                {
                    await _operationLogger.LogAsync(firstItem.batchId, firstItem.storeId, "info",
                        $"Bulk export handed off to durable integration operation {result.IntegrationOperationId}; awaiting confirmed outcome.");
                }
                result = await AwaitPendingBulkCommandAsync(
                    result, productIds, connectionId, linked.Token);
            }

            foreach (var itemResult in result.Items
                .GroupBy(r => r.ProductId)
                .Select(g => g.FirstOrDefault(x => x.IsSuccess) ?? g.Last()))
            {
                if (!pendingByProductId.TryGetValue(itemResult.ProductId, out var matchingItems))
                    continue;

                foreach (var pending in matchingItems)
                {
                    if (itemResult.IsSuccess)
                    {
                        pending.status = ProductExportStatus.Completed;
                        pending.completedAt = DateTime.UtcNow;
                        pending.externalProductId = itemResult.ExternalProductId;
                        pending.lastError = null;

                        _logger.LogDebug(
                            "Bulk: exported product {ProductId} -> External={ExternalId}",
                            pending.productId, itemResult.ExternalProductId);

                        if (pending.batchId != null)
                        {
                            var line = $"Exported: {OperationProductRef.Format(pending.productId, pending.productName)}";
                            if (string.IsNullOrWhiteSpace(itemResult.ErrorMessage))
                                await _operationLogger.LogAsync(pending.batchId, pending.storeId, "success", line);
                            else
                                await _operationLogger.LogAsync(pending.batchId, pending.storeId, "warning",
                                    $"{line}. {itemResult.ErrorMessage}");
                        }
                    }
                    else
                    {
                        pending.status = ProductExportStatus.Failed;
                        pending.lastError = itemResult.ErrorMessage ?? "Unknown error";

                        if (!itemResult.Retryable || IsPermanentError(pending.lastError))
                        {
                            pending.retryCount = PendingProductExport.MAX_RETRIES;
                            if (IsPermanentError(pending.lastError))
                                await CancelRemainingExportsAsync(context, pending, ct);
                        }

                        _logger.LogWarning(
                            "Bulk: failed to export product {ProductId}: {Error}",
                            pending.productId, pending.lastError);

                        if (pending.batchId != null)
                        {
                            var subject = OperationProductRef.Format(pending.productId, pending.productName);
                            var line = itemResult.Retryable
                                ? $"Error: {subject} - {pending.lastError}"
                                : $"{subject} - {pending.lastError}";
                            await _operationLogger.LogAsync(
                                pending.batchId, pending.storeId, itemResult.Retryable ? "error" : "warning", line);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            foreach (var pending in pendingItems.Where(p => p.status == ProductExportStatus.InProgress))
                pending.status = ProductExportStatus.Cancelled;
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
        finally
        {
            await linked.CancelAsync();
            try { await watcher; } catch { /* ignored */ }
        }

        await context.SaveChangesAsync(ct);

        // Emit progress/finish via SignalR for bulk export
        if (firstItem.batchId != null)
        {
            var batchItems = await context.pendingProductExports
                .AsNoTracking()
                .Where(p => p.batchId == firstItem.batchId)
                .ToListAsync(ct);
            var total = batchItems.Count;
            var completed = batchItems.Count(p => p.status == ProductExportStatus.Completed);
            var failed = batchItems.Count(IsTerminalFailure);
            var cancelled = batchItems.Count(p => p.status == ProductExportStatus.Cancelled);
            await _operationLogger.ProgressAsync(firstItem.batchId, firstItem.storeId, total, completed, failed);

            var pending = total - completed - failed - cancelled;
            if (pending == 0)
            {
                var status = failed > 0
                    ? (completed == 0 && cancelled == 0 ? "failed" : "partial")
                    : (cancelled > 0
                        ? (completed == 0 ? "cancelled" : "partial")
                        : "completed");
                await _operationLogger.FinishAsync(firstItem.batchId, firstItem.storeId, status,
                    $"Exported: {completed} succeeded, {failed} failed");
            }
        }
    }

    private static bool IsTerminalFailure(PendingProductExport item) =>
        item.status == ProductExportStatus.Failed &&
        item.retryCount >= PendingProductExport.MAX_RETRIES;

    private async Task CancelRemainingExportsAsync(
        DataContext context,
        PendingProductExport triggerItem,
        CancellationToken ct)
    {
        _logger.LogWarning(
            "Detected a permanent error for connection {ConnectionId}, cancelling remaining exports",
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
            _logger.LogWarning("Cancelled {Count} remaining exports for connection {ConnectionId}",
                remaining.Count, triggerItem.connectionId);
        }
    }

    /// <summary>
    /// Returns true if the error is permanent (account-level) and retrying won't help.
    /// Matches the Russian quota-exceeded phrase some marketplace adapters (e.g. Ozon) return.
    /// </summary>
    private static bool IsPermanentError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return false;
        return errorMessage.Contains("\u0438\u0441\u0447\u0435\u0440\u043f\u0430\u043b\u0438 \u043b\u0438\u043c\u0438\u0442", StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>true if a permanent (non-retryable) error was encountered.</returns>
    private async Task<bool> ProcessSingleExportAsync(
        DataContext context,
        ProductSyncService productSyncService,
        PendingProductExport pending,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Starting export of product {Id}:{ProductId} (current status: {Status})",
            pending.Id, pending.productId, pending.status);

        pending.status = ProductExportStatus.InProgress;
        pending.lastAttemptAt = DateTime.UtcNow;
        pending.retryCount++;
        await context.SaveChangesAsync(ct);

        _logger.LogDebug("Export status {Id} changed to InProgress", pending.Id);

        try
        {
            // Export the product
            var singleRequestKey = $"pending-export:{pending.Id}";
            var result = await productSyncService.ExportProductAsync(
                pending.productId,
                pending.connectionId,
                ct,
                requestKey: singleRequestKey);

            if (result.IsPending && !string.IsNullOrWhiteSpace(result.IntegrationCommandId))
            {
                if (pending.batchId != null)
                {
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId, "info",
                        $"Export of {OperationProductRef.Format(pending.productId, pending.productName)} handed off to durable operation {result.IntegrationOperationId}; awaiting confirmation.");
                }
                result = await AwaitPendingSingleCommandAsync(result, pending.productId, pending.connectionId, ct);
            }

            _logger.LogDebug(
                "Product export result {Id}: IsSuccess={IsSuccess}, Error={Error}",
                pending.Id, result.IsSuccess, result.ErrorMessage);

            if (result.IsSuccess && !result.IsPending)
            {
                pending.status = ProductExportStatus.Completed;
                pending.completedAt = DateTime.UtcNow;
                pending.externalProductId = result.ExternalProductId;
                pending.linkId = result.LinkId;
                pending.lastError = null;

                _logger.LogDebug(
                    "Exported product {ProductName} (ID: {ProductId}, External: {ExternalId}) from the queue",
                    pending.productName, pending.productId, result.ExternalProductId);

                if (pending.batchId != null)
                    await _operationLogger.LogAsync(pending.batchId, pending.storeId, "success",
                        $"Exported: {OperationProductRef.Format(pending.productId, pending.productName)}");
            }
            else
            {
                pending.status = ProductExportStatus.Failed;
                pending.lastError = result.ErrorMessage ?? "Unknown error";

                if (!result.Retryable || IsPermanentError(pending.lastError))
                    pending.retryCount = PendingProductExport.MAX_RETRIES;

                _logger.LogWarning(
                    "Failed to export product {ProductId}: {Error} (attempt {Retry})",
                    pending.productId, pending.lastError, pending.retryCount);

                if (pending.batchId != null)
                {
                    var subject = OperationProductRef.Format(pending.productId, pending.productName);
                    var line = result.Retryable
                        ? $"Error: {subject} - {pending.lastError}"
                        : $"{subject} - {pending.lastError}";
                    await _operationLogger.LogAsync(
                        pending.batchId, pending.storeId, result.Retryable ? "error" : "warning", line);
                }
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
                var failed = batchItems.Count(IsTerminalFailure);
                var cancelled = batchItems.Count(p => p.status == ProductExportStatus.Cancelled);
                await _operationLogger.ProgressAsync(pending.batchId, pending.storeId, total, completed, failed);

                if (total == completed + failed + cancelled)
                {
                    var status = failed > 0
                        ? (completed == 0 && cancelled == 0 ? "failed" : "partial")
                        : (cancelled > 0
                            ? (completed == 0 ? "cancelled" : "partial")
                            : "completed");
                    await _operationLogger.FinishAsync(pending.batchId, pending.storeId, status,
                        $"Exported: {completed} succeeded, {failed} failed");
                }
            }

            _logger.LogInformation(
                "Product {Id}:{ProductId} processed, final status: {Status}",
                pending.Id, pending.productId, pending.status);

            return IsPermanentError(pending.lastError);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            pending.status = ProductExportStatus.Failed;
            pending.lastError = ex.Message;

            var permanent = IsPermanentError(ex.Message);
            if (permanent)
                pending.retryCount = PendingProductExport.MAX_RETRIES;

            await context.SaveChangesAsync(CancellationToken.None); // Save even if cancelled

            _logger.LogError(ex,
                "Error exporting product {Id}:{ProductId} (attempt {Retry})",
                pending.Id, pending.productId, pending.retryCount);

            return permanent;
        }
    }

    private async Task<ProductExportResult> AwaitPendingSingleCommandAsync(
        ProductExportResult pendingResult,
        int productId,
        int connectionId,
        CancellationToken ct)
    {
        var commandId = pendingResult.IntegrationCommandId!;
        var terminal = await PollCommandUntilTerminalAsync(commandId, pendingResult.IntegrationOperationId, ct);
        if (terminal is not { } s)
            return ProductExportResult.Failure("Durable product export command was not found", retryable: false);

        var itemKey = $"product:{productId}";
        var itemStages = s.Stages.Where(st => st.itemKey == itemKey).ToList();
        var applied = itemStages.Count > 0
            && itemStages.All(st => st.state == Delegat.Models.Integration.IntegrationStageState.Applied);
        if (applied)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var link = await db.productMarketplaceLinks.AsNoTracking()
                .FirstOrDefaultAsync(l => l.productId == productId && l.connectionId == connectionId, ct);
            var extId = link?.externalProductId
                ?? Delegat.Services.Integration.IntegrationCommandResultExtractor.ExtractItemExternalId(itemStages, itemKey);
            if (!string.IsNullOrWhiteSpace(extId))
            {
                return ProductExportResult.Success(
                    link?.Id ?? 0,
                    extId,
                    isNew: false,
                    "Product exported via durable integration command",
                    pendingResult.IntegrationOperationId,
                    commandId);
            }
        }

        return ProductExportResult.Failure(
            s.Command.summary ?? "Durable product export did not confirm applied state",
            retryable: false);
    }

    private async Task<BulkProductExportResult> AwaitPendingBulkCommandAsync(
        BulkProductExportResult pendingResult,
        List<int> productIds,
        int connectionId,
        CancellationToken ct)
    {
        var commandId = pendingResult.IntegrationCommandId!;
        var terminal = await PollCommandUntilTerminalAsync(commandId, pendingResult.IntegrationOperationId, ct);
        if (terminal is not { } s)
        {
            var missingItems = productIds
                .Select(pid => new BulkProductExportItemResult(pid, false, ErrorMessage: "Durable bulk export command not found", Retryable: false))
                .ToList();
            return new BulkProductExportResult(0, missingItems.Count, productIds.Count, missingItems);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var links = await db.productMarketplaceLinks.AsNoTracking()
            .Where(l => l.connectionId == connectionId && productIds.Contains(l.productId))
            .ToDictionaryAsync(l => l.productId, ct);

        var stagesByItem = s.Stages
            .GroupBy(st => st.itemKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var preflightByProduct = pendingResult.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.First());

        var items = new List<BulkProductExportItemResult>(productIds.Count);
        var successCount = 0;
        var failedCount = 0;
        foreach (var pid in productIds)
        {
            if (preflightByProduct.TryGetValue(pid, out var preflight))
            {
                items.Add(preflight);
                if (preflight.IsSuccess) successCount++; else failedCount++;
                continue;
            }

            var itemKey = $"product:{pid}";
            var itemStages = stagesByItem.GetValueOrDefault(itemKey) ?? [];
            var applied = itemStages.Count > 0
                && itemStages.All(st => st.state == Delegat.Models.Integration.IntegrationStageState.Applied);
            if (applied)
            {
                var extId = (links.TryGetValue(pid, out var link) ? link.externalProductId : null)
                    ?? Delegat.Services.Integration.IntegrationCommandResultExtractor.ExtractItemExternalId(itemStages, itemKey);
                if (!string.IsNullOrWhiteSpace(extId))
                {
                    successCount++;
                    items.Add(new BulkProductExportItemResult(pid, true, ExternalProductId: extId));
                    continue;
                }
            }

            failedCount++;
            items.Add(new BulkProductExportItemResult(
                pid,
                false,
                ErrorMessage: s.Command.summary ?? "Durable product export did not confirm applied state",
                Retryable: false));
        }

        return new BulkProductExportResult(
            successCount,
            failedCount,
            productIds.Count,
            items,
            integrationOperationId: pendingResult.IntegrationOperationId,
            integrationCommandId: commandId);
    }

    private async Task<(Delegat.Models.Integration.IntegrationCommand Command, List<Delegat.Models.Integration.IntegrationStage> Stages)?> PollCommandUntilTerminalAsync(
        string commandId,
        string? operationId,
        CancellationToken ct)
    {
        while (true)
        {
            using var scope = _scopeFactory.CreateScope();
            var coordinator = scope.ServiceProvider.GetService<Delegat.Services.Integration.IntegrationCoordinator>();
            if (coordinator == null)
                return null;

            var snapshot = await coordinator.GetCommandSnapshotAsync(commandId, ct);
            if (snapshot == null || Delegat.Models.Integration.IntegrationCommandState.IsTerminal(snapshot.Value.Command.state))
                return snapshot;

            try
            {
                await Task.Delay(POLL_INTERVAL_MS, ct);
            }
            catch (OperationCanceledException)
            {
                if (!string.IsNullOrWhiteSpace(operationId))
                {
                    using var stopScope = _scopeFactory.CreateScope();
                    var stopCoordinator = stopScope.ServiceProvider.GetService<Delegat.Services.Integration.IntegrationCoordinator>();
                    if (stopCoordinator != null)
                    {
                        try { await stopCoordinator.RequestStopByOperationIdAsync(operationId, CancellationToken.None); }
                        catch { /* best effort */ }
                    }
                }
                throw;
            }
        }
    }
}
