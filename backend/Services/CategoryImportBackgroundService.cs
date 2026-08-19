using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;

namespace Delegat.Services;

/// <summary>
/// Processes queued marketplace category imports off the HTTP request.
/// </summary>
public sealed class CategoryImportBackgroundService : BackgroundService
{
    private const int PollIntervalMs = 1500;
    private const int ErrorDelayMs = 10000;
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CategoryImportBackgroundService> _logger;
    private readonly IOperationLogger _operationLogger;

    public CategoryImportBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CategoryImportBackgroundService> logger,
        IOperationLogger operationLogger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _operationLogger = operationLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CategoryImportBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
                await Task.Delay(PollIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CategoryImportBackgroundService");
                await Task.Delay(ErrorDelayMs, stoppingToken);
            }
        }

        _logger.LogInformation("CategoryImportBackgroundService stopped");
    }

    internal async Task ProcessOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var importService = scope.ServiceProvider.GetRequiredService<CategoryImportService>();

        var staleBefore = DateTime.UtcNow - InProgressTimeout;
        var job = await context.pendingCategoryImports
            .Where(j => j.status == CategoryImportStatus.Pending
                         || (j.status == CategoryImportStatus.Failed && j.retryCount < PendingCategoryImport.MAX_RETRIES)
                         || (j.status == CategoryImportStatus.InProgress
                             && (j.lastAttemptAt ?? j.createdAt) < staleBefore))
            .OrderBy(j => j.createdAt)
            .FirstOrDefaultAsync(ct);

        if (job == null)
            return;

        var opStatus = await context.operations.AsNoTracking()
            .Where(o => o.operationId == job.operationId)
            .Select(o => o.status)
            .FirstOrDefaultAsync(ct);

        if (OperationLifecycle.IsStopRequested(opStatus))
        {
            job.status = CategoryImportStatus.Cancelled;
            job.completedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);
            if (opStatus == OperationLifecycle.Paused)
            {
                await _operationLogger.FinishAsync(job.operationId, job.storeId, "cancelled", "Stopped");
            }
            return;
        }

        job.status = CategoryImportStatus.InProgress;
        job.lastAttemptAt = DateTime.UtcNow;
        job.retryCount++;
        await context.SaveChangesAsync(ct);

        var label = job.externalCategoryName ?? job.localCategoryName ?? job.externalCategoryId;
        await _operationLogger.LogAsync(job.operationId, job.storeId, "info",
            $"Importing category: {label}...");

        try
        {
            var connection = await context.marketplaceConnections
                .FirstOrDefaultAsync(c => c.Id == job.connectionId, ct);
            if (connection == null)
            {
                await FailAsync(context, job, "Connection not found", ct);
                return;
            }

            List<string>? selectedKeys = null;
            if (!string.IsNullOrWhiteSpace(job.selectedFieldKeysJson))
            {
                selectedKeys = JsonSerializer.Deserialize<List<string>>(job.selectedFieldKeysJson);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watcher = OperationStopWatcher.WatchAsync(_scopeFactory, job.operationId, linked);
            CategoryImportResult result;
            try
            {
                result = await importService.ImportExternalCategoryAsync(
                    connection,
                    job.externalCategoryId,
                    job.localCategoryName,
                    job.externalCategoryName,
                    job.localCategoryId,
                    selectedKeys,
                    linked.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                job.status = CategoryImportStatus.Cancelled;
                job.completedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(CancellationToken.None);
                await _operationLogger.FinishAsync(job.operationId, job.storeId, "cancelled", "Stopped");
                return;
            }
            finally
            {
                await linked.CancelAsync();
                try { await watcher; } catch { /* ignored */ }
            }

            if (await OperationLifecycle.IsStopRequestedAsync(context, job.operationId, ct))
            {
                job.status = CategoryImportStatus.Cancelled;
                job.completedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);
                return;
            }

            if (!result.IsSuccess)
            {
                await FailAsync(context, job, result.ErrorMessage ?? "Import failed", ct);
                return;
            }

            job.status = CategoryImportStatus.Completed;
            job.completedAt = DateTime.UtcNow;
            job.lastError = null;
            job.resultLocalCategoryId = result.LocalCategoryId;
            job.resultMappingId = result.CategoryMappingId;
            await context.SaveChangesAsync(ct);

            await _operationLogger.LogAsync(job.operationId, job.storeId, "success",
                $"Category imported: {result.Message} (attributes: {result.AttributesCreated})");

            await MaybeFinishStandaloneAsync(context, job, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Category import job {JobId} failed", job.Id);
            await FailAsync(context, job, ex.Message, ct);
        }
    }

    private async Task FailAsync(
        DataContext context,
        PendingCategoryImport job,
        string error,
        CancellationToken ct)
    {
        job.status = CategoryImportStatus.Failed;
        job.lastError = error;
        await context.SaveChangesAsync(CancellationToken.None);

        await _operationLogger.LogAsync(job.operationId, job.storeId, "error",
            $"Failed to import category: {error}");

        if (job.retryCount >= PendingCategoryImport.MAX_RETRIES)
            await MaybeFinishStandaloneAsync(context, job, ct);
    }

    private async Task MaybeFinishStandaloneAsync(
        DataContext context,
        PendingCategoryImport job,
        CancellationToken ct)
    {
        var op = await context.operations.AsNoTracking()
            .Where(o => o.operationId == job.operationId)
            .Select(o => new { o.type, o.status })
            .FirstOrDefaultAsync(ct);

        if (op == null || op.type != "category-import")
            return;
        if (op.status is "completed" or "failed" or "cancelled" or "partial")
            return;

        var remaining = await context.pendingCategoryImports
            .AnyAsync(j => j.operationId == job.operationId
                           && (j.status == CategoryImportStatus.Pending
                               || j.status == CategoryImportStatus.InProgress
                               || (j.status == CategoryImportStatus.Failed
                                   && j.retryCount < PendingCategoryImport.MAX_RETRIES)),
                ct);
        if (remaining)
            return;

        var anyFailed = await context.pendingCategoryImports
            .AnyAsync(j => j.operationId == job.operationId && j.status == CategoryImportStatus.Failed, ct);

        await _operationLogger.FinishAsync(
            job.operationId,
            job.storeId,
            anyFailed ? "failed" : "completed",
            anyFailed ? job.lastError : "Category imported");
    }
}
