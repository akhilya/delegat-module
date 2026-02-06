using System.Collections.Concurrent;
using PrintO.Marketplace.SDK;

namespace PrintO.Services.Plugins;

/// <summary>
/// Manages plugin lifecycle: loading, unloading, and access.
/// Thread-safe singleton service.
/// </summary>
public class PluginManager : IDisposable
{
    private readonly ConcurrentDictionary<string, LoadedPlugin> _plugins = new();
    private readonly string _pluginsDirectory;
    private readonly ILogger<PluginManager> _logger;
    private bool _disposed;

    public PluginManager(IConfiguration configuration, ILogger<PluginManager> logger)
    {
        _logger = logger;
        _pluginsDirectory = configuration.GetValue<string>("Plugins:Directory")
            ?? Path.Combine(AppContext.BaseDirectory, "plugins");

        // Ensure directory exists
        if (!Directory.Exists(_pluginsDirectory))
        {
            Directory.CreateDirectory(_pluginsDirectory);
            _logger.LogInformation("Created plugins directory: {Directory}", _pluginsDirectory);
        }
    }

    /// <summary>
    /// Get plugins directory path.
    /// </summary>
    public string PluginsDirectory => _pluginsDirectory;

    /// <summary>
    /// Load all plugins from the plugins directory.
    /// </summary>
    public void LoadAllPlugins()
    {
        _logger.LogInformation("Loading plugins from {Directory}", _pluginsDirectory);

        var plugins = PluginLoader.LoadPluginsFromDirectory(_pluginsDirectory, _logger);

        foreach (var plugin in plugins)
        {
            if (_plugins.TryAdd(plugin.Key, plugin))
            {
                _logger.LogInformation("Registered plugin: {Key}", plugin.Key);
            }
            else
            {
                _logger.LogWarning("Plugin with key {Key} already exists, skipping", plugin.Key);
                plugin.Dispose();
            }
        }

        _logger.LogInformation("Loaded {Count} plugins", _plugins.Count);
    }

    /// <summary>
    /// Load a single plugin from a DLL file.
    /// </summary>
    public LoadedPlugin LoadPlugin(string dllPath)
    {
        var plugin = PluginLoader.LoadPlugin(dllPath);

        if (!_plugins.TryAdd(plugin.Key, plugin))
        {
            plugin.Dispose();
            throw new InvalidOperationException($"Plugin with key '{plugin.Key}' already exists");
        }

        _logger.LogInformation("Loaded plugin: {Key} from {Path}", plugin.Key, dllPath);
        return plugin;
    }

    /// <summary>
    /// Unload a plugin by key.
    /// </summary>
    public bool UnloadPlugin(string key)
    {
        if (_plugins.TryRemove(key, out var plugin))
        {
            plugin.Dispose();
            _logger.LogInformation("Unloaded plugin: {Key}", key);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reload a plugin (unload and load again).
    /// </summary>
    public LoadedPlugin? ReloadPlugin(string key)
    {
        if (!_plugins.TryGetValue(key, out var existing))
        {
            _logger.LogWarning("Cannot reload plugin {Key}: not found", key);
            return null;
        }

        var filePath = existing.FilePath;

        // Unload
        if (!_plugins.TryRemove(key, out _))
        {
            return null;
        }

        existing.Dispose();
        _logger.LogInformation("Unloaded plugin {Key} for reload", key);

        // Wait a bit for file to be released
        Thread.Sleep(100);

        // Load again
        try
        {
            var newPlugin = PluginLoader.LoadPlugin(filePath);
            if (_plugins.TryAdd(newPlugin.Key, newPlugin))
            {
                _logger.LogInformation("Reloaded plugin: {Key}", newPlugin.Key);
                return newPlugin;
            }
            else
            {
                newPlugin.Dispose();
                throw new InvalidOperationException($"Failed to re-register plugin '{key}'");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload plugin {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// Get a plugin by key.
    /// </summary>
    public IMarketplacePlugin? GetPlugin(string key)
    {
        return _plugins.TryGetValue(key, out var loaded) ? loaded.Plugin : null;
    }

    /// <summary>
    /// Get loaded plugin info by key.
    /// </summary>
    public LoadedPlugin? GetLoadedPlugin(string key)
    {
        return _plugins.TryGetValue(key, out var loaded) ? loaded : null;
    }

    /// <summary>
    /// Get all loaded plugins.
    /// </summary>
    public IEnumerable<IMarketplacePlugin> GetAllPlugins()
    {
        return _plugins.Values.Select(p => p.Plugin);
    }

    /// <summary>
    /// Get all loaded plugin info.
    /// </summary>
    public IEnumerable<LoadedPlugin> GetAllLoadedPlugins()
    {
        return _plugins.Values;
    }

    /// <summary>
    /// Check if a plugin is loaded.
    /// </summary>
    public bool IsPluginLoaded(string key)
    {
        return _plugins.ContainsKey(key);
    }

    /// <summary>
    /// Get count of loaded plugins.
    /// </summary>
    public int PluginCount => _plugins.Count;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var plugin in _plugins.Values)
        {
            try
            {
                plugin.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing plugin {Key}", plugin.Key);
            }
        }

        _plugins.Clear();
    }
}
