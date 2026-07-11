using Microsoft.Extensions.Logging;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK;
using Delegat.Services.Marketplace;

namespace Delegat.Services.Plugins;

/// <summary>
/// Implementation of IMarketplaceAdapterRegistry that uses plugins.
/// Wraps plugins in PluginAdapterWrapper to provide IMarketplaceAdapter interface.
/// </summary>
public class PluginAdapterRegistry : IMarketplaceAdapterRegistry
{
    private readonly PluginManager _pluginManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly EncryptionService? _encryptionService;
    private readonly Dictionary<string, PluginAdapterWrapper> _wrapperCache = new();
    private readonly object _cacheLock = new();

    public PluginAdapterRegistry(PluginManager pluginManager, ILoggerFactory loggerFactory, EncryptionService? encryptionService = null)
    {
        _pluginManager = pluginManager;
        _loggerFactory = loggerFactory;
        _encryptionService = encryptionService;
    }

    public IReadOnlyList<IMarketplaceAdapter> GetAll()
    {
        var plugins = _pluginManager.GetAllPlugins().ToList();
        var adapters = new List<IMarketplaceAdapter>();

        foreach (var plugin in plugins)
        {
            adapters.Add(GetOrCreateWrapper(plugin.Key));
        }

        return adapters;
    }

    public IMarketplaceAdapter? GetByKey(string key)
    {
        var plugin = _pluginManager.GetPlugin(key);
        if (plugin == null) return null;

        return GetOrCreateWrapper(key);
    }

    public bool Exists(string key)
    {
        return _pluginManager.IsPluginLoaded(key);
    }

    public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces()
    {
        return _pluginManager.GetAllPlugins().Select(plugin => new MarketplaceInfo
        {
            Key = plugin.Key,
            DisplayName = plugin.DisplayName,
            Description = plugin.Description,
            LogoUrl = plugin.LogoUrl,
            Capabilities = (Marketplace.MarketplaceCapabilities)(int)plugin.Capabilities,
            CredentialsSchema = ConvertCredentialsSchema(plugin.GetCredentialsSchema())
        }).ToList();
    }

    /// <summary>
    /// Get or create a wrapper for a plugin.
    /// </summary>
    private PluginAdapterWrapper GetOrCreateWrapper(string key)
    {
        lock (_cacheLock)
        {
            if (_wrapperCache.TryGetValue(key, out var existingWrapper))
            {
                // Check if plugin is still the same (not reloaded)
                var plugin = _pluginManager.GetPlugin(key);
                if (plugin != null)
                {
                    return existingWrapper;
                }

                // Plugin was reloaded, remove old wrapper
                _wrapperCache.Remove(key);
            }

            var currentPlugin = _pluginManager.GetPlugin(key);
            if (currentPlugin == null)
            {
                throw new KeyNotFoundException($"Plugin '{key}' not found");
            }

            var wrapper = new PluginAdapterWrapper(currentPlugin, _loggerFactory, _encryptionService);
            _wrapperCache[key] = wrapper;
            return wrapper;
        }
    }

    /// <summary>
    /// Invalidate wrapper cache when a plugin is reloaded.
    /// </summary>
    public void InvalidateCache(string key)
    {
        lock (_cacheLock)
        {
            _wrapperCache.Remove(key);
        }
    }

    /// <summary>
    /// Clear all cached wrappers.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _wrapperCache.Clear();
        }
    }

    private static Marketplace.CredentialsSchema ConvertCredentialsSchema(Delegat.Marketplace.SDK.CredentialsSchema schema)
    {
        return new Marketplace.CredentialsSchema
        {
            Fields = schema.Fields.Select(f => new Marketplace.CredentialField
            {
                Key = f.Key,
                DisplayName = f.DisplayName,
                Description = f.Description,
                Type = f.Type,
                IsRequired = f.IsRequired,
                IsSecret = f.IsSecret,
                DefaultValue = f.DefaultValue,
                Placeholder = f.Placeholder,
                Options = f.Options?.Select(o => new Marketplace.SelectOption
                {
                    Value = o.Value,
                    Label = o.Label
                }).ToList()
            }).ToList()
        };
    }
}
