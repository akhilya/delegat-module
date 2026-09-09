using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Services.Marketplace;

namespace Delegat.Services;

/// <summary>
/// Processes Catalog Connect import/match/apply jobs so a page reload cannot abort the scan.
/// </summary>
public sealed class CatalogConnectBackgroundService : BackgroundService
{
    private const int PollIntervalMs = 1500;
    private const int ErrorDelayMs = 15000;
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogConnectBackgroundService> _logger;

    public CatalogConnectBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CatalogConnectBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CatalogConnectBackgroundService started");

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
                _logger.LogError(ex, "Error in CatalogConnectBackgroundService");
                await Task.Delay(ErrorDelayMs, stoppingToken);
            }
        }

        _logger.LogInformation("CatalogConnectBackgroundService stopped");
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var jobService = scope.ServiceProvider.GetRequiredService<CatalogConnectJobService>();

        var staleBefore = DateTime.UtcNow - InProgressTimeout;
        var stale = await context.pendingCatalogConnectJobs
            .Where(j => j.status == CatalogConnectJobStatus.InProgress
                        && (j.lastAttemptAt ?? j.createdAt) < staleBefore)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            var logger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();
            foreach (var stuck in stale)
            {
                stuck.status = CatalogConnectJobStatus.Failed;
                stuck.lastError = "Timed out";
                stuck.completedAt = DateTime.UtcNow;
                await logger.FinishAsync(stuck.operationId, stuck.storeId, "failed", "Timed out");
            }
            await context.SaveChangesAsync(ct);
        }

        var snapshot = await context.pendingCatalogConnectJobs
            .AsNoTracking()
            .Where(j => j.status == CatalogConnectJobStatus.Pending
                        || j.status == CatalogConnectJobStatus.InProgress)
            .ToListAsync(ct);
        var pick = PickNext(snapshot);
        if (pick == null)
            return;

        var job = await context.pendingCatalogConnectJobs
            .FirstOrDefaultAsync(j => j.Id == pick.Id, ct);
        if (job == null || job.status != CatalogConnectJobStatus.Pending)
            return;

        if (job.status == CatalogConnectJobStatus.Pending)
        {
            var opStatus = await context.operations.AsNoTracking()
                .Where(o => o.operationId == job.operationId)
                .Select(o => o.status)
                .FirstOrDefaultAsync(ct);

            if (OperationLifecycle.IsStopRequested(opStatus))
            {
                job.status = CatalogConnectJobStatus.Cancelled;
                job.completedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);
                var logger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();
                await logger.FinishAsync(job.operationId, job.storeId, "cancelled", "Stopped");
                return;
            }
        }

        _logger.LogInformation("Processing catalog connect job {JobId} ({Kind})", job.Id, job.kind);
        await jobService.ProcessJobAsync(job, ct);
    }

    /// <summary>
    /// One Catalog Connect job runs at a time per store. Other stores can proceed.
    /// </summary>
    internal static PendingCatalogConnectJob? PickNext(IReadOnlyList<PendingCatalogConnectJob> jobs)
    {
        var busyStores = jobs
            .Where(j => j.status == CatalogConnectJobStatus.InProgress)
            .Select(j => j.storeId)
            .ToHashSet();
        return jobs
            .Where(j => j.status == CatalogConnectJobStatus.Pending && !busyStores.Contains(j.storeId))
            .OrderBy(j => j.createdAt)
            .ThenBy(j => j.Id)
            .FirstOrDefault();
    }
}
