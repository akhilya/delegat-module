using System.Reflection;
using Delegat.Marketplace.SDK;

namespace Delegat.Services.Plugins;

/// <summary>
/// Loads plugin instances from DLL files.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Load a plugin from a DLL file.
    /// </summary>
    /// <param name="pluginPath">Full path to the plugin DLL</param>
    /// <returns>Loaded plugin info including the plugin instance and load context</returns>
    public static LoadedPlugin LoadPlugin(string pluginPath)
    {
        if (!File.Exists(pluginPath))
        {
            throw new FileNotFoundException($"Plugin file not found: {pluginPath}");
        }

        // Create isolated load context
        var loadContext = new PluginLoadContext(pluginPath);

        try
        {
            // Load the assembly from memory (doesn't lock the file)
            var assembly = loadContext.LoadFromFileWithoutLock(pluginPath);

            // Find plugin entry type (MarketplacePluginBase subclass).
            // Use base-type name matching instead of IsAssignableFrom because plugins
            // may be compiled against a different SDK copy than the host assembly.
            var pluginType = GetLoadableTypes(assembly)
                .FirstOrDefault(IsMarketplacePluginType);

            if (pluginType == null)
            {
                loadContext.Unload();
                throw new InvalidOperationException(
                    $"Plugin assembly does not contain a type implementing IMarketplacePlugin: {pluginPath}");
            }

            // Create instance
            var plugin = (IMarketplacePlugin?)Activator.CreateInstance(pluginType);
            if (plugin == null)
            {
                loadContext.Unload();
                throw new InvalidOperationException(
                    $"Failed to create plugin instance from type {pluginType.FullName}");
            }

            return new LoadedPlugin(
                plugin.Key,
                plugin,
                loadContext,
                pluginPath,
                plugin.Version,
                DateTime.UtcNow
            );
        }
        catch (Exception)
        {
            // Unload context on error
            loadContext.Unload();
            throw;
        }
    }

    /// <summary>
    /// Scan a directory for plugin DLLs and load them all.
    /// </summary>
    /// <param name="pluginsDirectory">Directory containing plugin DLLs</param>
    /// <param name="logger">Optional logger for errors</param>
    /// <returns>List of loaded plugins</returns>
    public static List<LoadedPlugin> LoadPluginsFromDirectory(
        string pluginsDirectory,
        ILogger? logger = null)
    {
        var plugins = new List<LoadedPlugin>();

        if (!Directory.Exists(pluginsDirectory))
        {
            logger?.LogWarning("Plugins directory does not exist: {Directory}", pluginsDirectory);
            return plugins;
        }

        var dllFiles = Directory.GetFiles(pluginsDirectory, "Delegat.Marketplace.*.dll", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).Equals("Delegat.Marketplace.SDK.dll", StringComparison.OrdinalIgnoreCase));

        foreach (var dllPath in dllFiles)
        {
            try
            {
                var plugin = LoadPlugin(dllPath);
                plugins.Add(plugin);
                logger?.LogInformation(
                    "Loaded plugin: {Key} ({DisplayName}) v{Version} from {Path}",
                    plugin.Key, plugin.Plugin.DisplayName, plugin.Version, dllPath);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to load plugin from {Path}", dllPath);
            }
        }

        return plugins;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }

    private static bool IsMarketplacePluginType(Type type)
    {
        if (type.IsInterface || type.IsAbstract)
            return false;

        if (typeof(IMarketplacePlugin).IsAssignableFrom(type))
            return true;

        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.FullName == typeof(MarketplacePluginBase).FullName)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Represents a loaded plugin with its context.
/// </summary>
public class LoadedPlugin : IDisposable
{
    public string Key { get; }
    public IMarketplacePlugin Plugin { get; }
    public PluginLoadContext LoadContext { get; }
    public string FilePath { get; }
    public string Version { get; }
    public DateTime LoadedAt { get; }

    private bool _disposed;

    public LoadedPlugin(
        string key,
        IMarketplacePlugin plugin,
        PluginLoadContext loadContext,
        string filePath,
        string version,
        DateTime loadedAt)
    {
        Key = key;
        Plugin = plugin;
        LoadContext = loadContext;
        FilePath = filePath;
        Version = version;
        LoadedAt = loadedAt;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unload the assembly context to free resources
        LoadContext.Unload();

        // Trigger GC to help clean up
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
