namespace PrintO.Services.Plugins;

/// <summary>
/// Hosted service that loads all plugins at application startup.
/// </summary>
public class PluginHostedService : IHostedService
{
    private readonly PluginManager _pluginManager;
    private readonly ILogger<PluginHostedService> _logger;

    public PluginHostedService(PluginManager pluginManager, ILogger<PluginHostedService> logger)
    {
        _pluginManager = pluginManager;
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
        }

        return Task.CompletedTask;
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
