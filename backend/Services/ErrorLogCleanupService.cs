using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>Bounds the size of persisted exception logs by age and row count.</summary>
public sealed class ErrorLogCleanupService : BackgroundService
{
    private const int RetentionDays = 14;
    private const int MaxRows = 50_000;
    private const int MaxDeletesPerRun = 100_000;
    private const int BatchSize = 5_000;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErrorLogCleanupService> _logger;

    public ErrorLogCleanupService(IServiceScopeFactory scopeFactory, ILogger<ErrorLogCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var removed = await DeleteOldestAsync(db, DateTime.UtcNow.AddDays(-RetentionDays), MaxDeletesPerRun, stoppingToken);
                var total = await db.errorLogs.CountAsync(stoppingToken);
                if (total > MaxRows)
                    removed += await DeleteOldestAsync(db, null, Math.Min(total - MaxRows, MaxDeletesPerRun), stoppingToken);
                if (removed > 0)
                    _logger.LogInformation("Error log cleanup removed {Count} rows (retention {Days}d, cap {MaxRows})", removed, RetentionDays, MaxRows);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Error log cleanup failed"); }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private static async Task<int> DeleteOldestAsync(DataContext db, DateTime? before, int maxRows, CancellationToken ct)
    {
        var deleted = 0;
        while (deleted < maxRows)
        {
            var query = db.errorLogs.AsNoTracking();
            if (before.HasValue) query = query.Where(log => log.createdAt < before.Value);
            var ids = await query.OrderBy(log => log.createdAt).ThenBy(log => log.Id)
                .Select(log => log.Id).Take(Math.Min(BatchSize, maxRows - deleted)).ToListAsync(ct);
            if (ids.Count == 0) break;
            if (db.Database.IsRelational())
                deleted += await db.errorLogs.Where(log => ids.Contains(log.Id)).ExecuteDeleteAsync(ct);
            else
            {
                var rows = await db.errorLogs.Where(log => ids.Contains(log.Id)).ToListAsync(ct);
                db.errorLogs.RemoveRange(rows);
                deleted += rows.Count;
                await db.SaveChangesAsync(ct);
            }
        }
        return deleted;
    }
}
