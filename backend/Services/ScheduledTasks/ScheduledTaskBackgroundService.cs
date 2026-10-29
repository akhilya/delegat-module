using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;

namespace Delegat.Services.ScheduledTasks;

/// <summary>Claims due schedules and invokes only explicitly registered actions.</summary>
public sealed class ScheduledTaskBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledTaskBackgroundService> _logger;

    public ScheduledTaskBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledTaskBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled task worker failed");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    private async Task ProcessDueAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var actions = scope.ServiceProvider.GetServices<IScheduledTaskAction>()
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var due = await db.scheduledTasks.AsNoTracking()
            .Where(x => x.isEnabled && x.nextRunAt <= now
                        && (x.executionLeaseExpiresAtUtc == null || x.executionLeaseExpiresAtUtc <= now))
            .OrderBy(x => x.nextRunAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var task in due)
        {
            ct.ThrowIfCancellationRequested();
            var scheduledFor = task.nextRunAt;
            var nextRun = DateTime.UtcNow.AddMinutes(task.intervalMinutes);
            var leaseExpiresAt = DateTime.UtcNow.AddHours(6);
            // Atomic compare-and-set prevents duplicate claims across multiple app instances.
            var claimed = await db.scheduledTasks
                .Where(x => x.Id == task.Id && x.isEnabled && x.nextRunAt == scheduledFor
                            && (x.executionLeaseExpiresAtUtc == null || x.executionLeaseExpiresAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.nextRunAt, nextRun)
                    .SetProperty(x => x.executionLeaseExpiresAtUtc, leaseExpiresAt)
                    .SetProperty(x => x.updatedAt, DateTime.UtcNow), ct);
            if (claimed == 0)
                continue;

            var run = new ScheduledTaskRun
            {
                scheduledTaskId = task.Id,
                storeId = task.storeId,
                actionId = task.actionId,
                scheduledFor = scheduledFor,
                startedAt = DateTime.UtcNow,
            };
            db.scheduledTaskRuns.Add(run);
            await db.SaveChangesAsync(ct);

            try
            {
                if (!actions.TryGetValue(task.actionId, out var action))
                    throw new InvalidOperationException($"Scheduled action '{task.actionId}' is not registered.");
                using var parameters = JsonDocument.Parse(task.parametersJson);
                if (parameters.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Scheduled action parameters must be a JSON object.");

                await action.ExecuteAsync(task, ct);
                run.status = "succeeded";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                run.status = "failed";
                run.error = "Worker stopped during execution.";
            }
            catch (Exception ex)
            {
                run.status = "failed";
                run.error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                _logger.LogError(ex, "Scheduled task {TaskId} action {ActionId} failed", task.Id, task.actionId);
            }

            run.finishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await db.scheduledTasks
                .Where(x => x.Id == task.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.executionLeaseExpiresAtUtc, (DateTime?)null), CancellationToken.None);
        }
    }
}
