using Delegat.Infrastructure.Configuration;

namespace Delegat.Services.PlatformLink;

/// <summary>
/// Pulls Reference Catalog from Control Plane on startup (if empty / linked)
/// and on a refresh interval.
/// </summary>
public sealed class ReferenceCatalogSyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformLinkOptions _options;
    private readonly ILogger<ReferenceCatalogSyncHostedService> _logger;

    public ReferenceCatalogSyncHostedService(
        IServiceScopeFactory scopeFactory,
        PlatformLinkOptions options,
        ILogger<ReferenceCatalogSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
        {
            _logger.LogInformation("Platform Link disabled; reference catalog sync will not run");
            return;
        }

        // Brief delay so migrations / DB are ready
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var catalog = scope.ServiceProvider.GetRequiredService<IReferenceCatalogService>();
                await catalog.SyncFromControlPlaneAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Reference catalog sync failed");
            }

            var delay = TimeSpan.FromMinutes(_options.CatalogRefreshMinutes);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
