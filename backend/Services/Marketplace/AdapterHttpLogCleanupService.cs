namespace Delegat.Services.Marketplace;

/// <summary>
/// Periodically purges old adapter HTTP logs by retention policy.
/// </summary>
public sealed class AdapterHttpLogCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdapterHttpLogCleanupService> _logger;

    public AdapterHttpLogCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AdapterHttpLogCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AdapterHttpLogCleanupService started");
        // Initial delay so startup migrations finish
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logs = scope.ServiceProvider.GetRequiredService<AdapterHttpLogService>();
                await logs.CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Adapter HTTP log cleanup failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }

        _logger.LogInformation("AdapterHttpLogCleanupService stopped");
    }
}
