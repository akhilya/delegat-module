namespace Delegat.Services.Plugins;

/// <summary>
/// Hosted service that loads all plugins at application startup
/// and syncs plugin field mappings when versions change.
/// </summary>
public class PluginHostedService : IHostedService
{
    private readonly PluginManager _pluginManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PluginHostedService> _logger;

    public PluginHostedService(
        PluginManager pluginManager,
        IServiceScopeFactory scopeFactory,
        ILogger<PluginHostedService> logger)
    {
        _pluginManager = pluginManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
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
            return;
        }

        // Sync plugin versions and add missing fields to mappings
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<PluginVersionSyncService>();
            await syncService.SyncAllPluginVersionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin version sync failed");
            // Don't block app startup
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Plugin system stopping...");

        try
        {
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
