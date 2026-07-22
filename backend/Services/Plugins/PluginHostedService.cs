namespace Delegat.Services.Plugins;

/// <summary>
/// Hosted service that loads all plugins at application startup
/// and syncs plugin field mappings when versions change.
/// </summary>
public class PluginHostedService : IHostedService
{
    private readonly PluginManager _pluginManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PluginHostedService> _logger;
    private CancellationTokenSource? _syncCts;

    public PluginHostedService(
        PluginManager pluginManager,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        ILogger<PluginHostedService> logger)
    {
        _pluginManager = pluginManager;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Plugin system starting...");

        try
        {
            _pluginManager.LoadAllPlugins();
            _logger.LogInformation("Plugin system started. Loaded {Count} plugins.", _pluginManager.PluginCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin system startup");
            return Task.CompletedTask;
        }

        // Version sync may call marketplace APIs (and wait on 429). Do it after Kestrel is listening
        // so the SPA proxy does not get ECONNREFUSED during boot.
        _syncCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var syncToken = _syncCts.Token;
        _lifetime.ApplicationStarted.Register(() =>
        {
            _ = SyncPluginVersionsInBackgroundAsync(syncToken);
        });

        return Task.CompletedTask;
    }

    private async Task SyncPluginVersionsInBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<PluginVersionSyncService>();
            await syncService.SyncAllPluginVersionsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin version sync failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Plugin system stopping...");

        try
        {
            _syncCts?.Cancel();
            _syncCts?.Dispose();
            _syncCts = null;
            _pluginManager.Dispose();
            _logger.LogInformation("Plugin system stopped.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin system shutdown");
        }

        return Task.CompletedTask;
    }
}
