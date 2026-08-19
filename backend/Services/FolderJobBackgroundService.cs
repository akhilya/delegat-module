using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Services.FolderJobs;

namespace Delegat.Services;

/// <summary>
/// Processes pending folder archive/delete jobs and cleans up expired ZIP artifacts.
/// </summary>
public sealed class FolderJobBackgroundService : BackgroundService
{
    private const int PollIntervalMs = 3000;
    private const int ErrorDelayMs = 15000;
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FolderJobBackgroundService> _logger;

    public FolderJobBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<FolderJobBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FolderJobBackgroundService started");

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
                _logger.LogError(ex, "Error in FolderJobBackgroundService");
                await Task.Delay(ErrorDelayMs, stoppingToken);
            }
        }

        _logger.LogInformation("FolderJobBackgroundService stopped");
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var jobService = scope.ServiceProvider.GetRequiredService<FolderJobService>();

        await jobService.CleanupExpiredArtifactsAsync(ct);

        var staleBefore = DateTime.UtcNow - InProgressTimeout;
        var job = await context.pendingFolderJobs
            .Where(j => j.status == FolderJobStatus.Pending
                        || (j.status == FolderJobStatus.InProgress
                            && (j.lastAttemptAt ?? j.createdAt) < staleBefore))
            .OrderBy(j => j.createdAt)
            .FirstOrDefaultAsync(ct);

        if (job == null)
            return;

        if (job.status == FolderJobStatus.Pending)
        {
            var opStatus = await context.operations.AsNoTracking()
                .Where(o => o.operationId == job.operationId)
                .Select(o => o.status)
                .FirstOrDefaultAsync(ct);

            if (OperationLifecycle.IsStopRequested(opStatus))
            {
                job.status = FolderJobStatus.Cancelled;
                job.completedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);
                var logger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();
                await logger.FinishAsync(job.operationId, job.storeId, "cancelled", "Stopped");
                return;
            }
        }

        _logger.LogInformation("Processing folder job {JobId} ({Type})", job.Id, job.jobType);
        await jobService.ProcessJobAsync(job, ct);
    }
}
