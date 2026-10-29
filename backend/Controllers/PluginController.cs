using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Services.Plugins;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("/plugins")]
public class PluginController : ControllerBase
{
    private readonly PluginManager _pluginManager;
    private readonly PluginAdapterRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PluginController> _logger;

    public PluginController(
        PluginManager pluginManager,
        PluginAdapterRegistry registry,
        IServiceScopeFactory scopeFactory,
        ILogger<PluginController> logger)
    {
        _pluginManager = pluginManager;
        _registry = registry;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private static void EnsurePluginsManagementEnabled()
    {
        if (!FeatureFlags.IsPluginsEnabled())
            throw ApiException.NotFound(code: "plugins_disabled");
    }

    /// <summary>
    /// Get list of all installed plugins
    /// </summary>
    [HttpGet]
    public IActionResult GetPlugins()
    {
        EnsurePluginsManagementEnabled();
        var plugins = _pluginManager.GetAllLoadedPlugins().Select(p => new
        {
            key = p.Key,
            displayName = p.Plugin.DisplayName,
            version = p.Version,
            stability = p.Plugin.Stability.ToString(),
            description = p.Plugin.Description,
            logoUrl = p.Plugin.LogoUrl,
            capabilities = (int)p.Plugin.Capabilities,
            isSystem = p.Plugin.IsSystem,
            filePath = Path.GetFileName(p.FilePath),
            loadedAt = p.LoadedAt
        }).ToList();

        return Ok(plugins);
    }

    /// <summary>
    /// Get plugin info by key
    /// </summary>
    [HttpGet("{key}")]
    public IActionResult GetPlugin(string key)
    {
        EnsurePluginsManagementEnabled();
        var loaded = _pluginManager.GetLoadedPlugin(key);
        if (loaded == null)
            throw ApiException.NotFound(code: "plugin_not_found");

        var plugin = loaded.Plugin;

        return Ok(new
        {
            key = plugin.Key,
            displayName = plugin.DisplayName,
            version = loaded.Version,
            stability = plugin.Stability.ToString(),
            description = plugin.Description,
            logoUrl = plugin.LogoUrl,
            capabilities = (int)plugin.Capabilities,
            isSystem = plugin.IsSystem,
            credentialsSchema = plugin.GetCredentialsSchema(),
            filePath = Path.GetFileName(loaded.FilePath),
            loadedAt = loaded.LoadedAt
        });
    }

    /// <summary>
    /// Upload and install a new plugin
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [RequiresPermission(Permissions.PluginsManage)]
    public async Task<IActionResult> UploadPlugin(IFormFile file)
    {
        EnsurePluginsManagementEnabled();
        if (file == null || file.Length == 0)
            throw ApiException.BadRequest(code: "plugin_file_required");

        var safeFileName = FileNameSafety.Sanitize(file.FileName, "plugin.dll");
        if (!safeFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest(code: "plugin_file_type_invalid");

        // Validate file size (max 50MB)
        if (file.Length > 50 * 1024 * 1024)
            throw ApiException.BadRequest(code: "plugin_file_too_large");

        var pluginsDir = _pluginManager.PluginsDirectory;
        var destPath = Path.GetFullPath(Path.Combine(pluginsDir, safeFileName));
        if (!destPath.StartsWith(Path.GetFullPath(pluginsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !destPath.Equals(Path.GetFullPath(pluginsDir), StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest(code: "plugin_file_name_invalid");

        // If file exists, check if plugin is loaded and unload it first
        if (System.IO.File.Exists(destPath))
        {
            // Find and unload any plugin using this file
            var existingPlugin = _pluginManager.GetAllLoadedPlugins()
                .FirstOrDefault(p => Path.GetFileName(p.FilePath).Equals(safeFileName, StringComparison.OrdinalIgnoreCase));

            if (existingPlugin != null)
            {
                _pluginManager.UnloadPlugin(existingPlugin.Key);
                _registry.InvalidateCache(existingPlugin.Key);
            }

            // Delete old file
            System.IO.File.Delete(destPath);
        }

        try
        {
            // Save file
            using (var stream = new FileStream(destPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Try to load the plugin
            var loaded = _pluginManager.LoadPlugin(destPath);

            _logger.LogInformation("Plugin uploaded and loaded: {Key} from {File}", loaded.Key, safeFileName);

            // Invalidate registry cache
            _registry.InvalidateCache(loaded.Key);

            return Ok(new
            {
                success = true,
                key = loaded.Key,
                displayName = loaded.Plugin.DisplayName,
                version = loaded.Version,
                message = $"Plugin '{loaded.Plugin.DisplayName}' installed successfully"
            });
        }
        catch (Exception ex)
        {
            // Clean up file on error
            if (System.IO.File.Exists(destPath))
            {
                try { System.IO.File.Delete(destPath); } catch { }
            }

            _logger.LogError(ex, "Failed to install plugin from {File}", safeFileName);
            throw ApiException.BadRequest(code: "plugin_load_failed");
        }
    }

    /// <summary>
    /// Delete a plugin
    /// </summary>
    [HttpDelete("{key}")]
    [RequiresPermission(Permissions.PluginsManage)]
    public IActionResult DeletePlugin(string key)
    {
        EnsurePluginsManagementEnabled();
        var loaded = _pluginManager.GetLoadedPlugin(key);
        if (loaded == null)
            throw ApiException.NotFound(code: "plugin_not_found");

        if (loaded.Plugin.IsSystem)
            throw ApiException.BadRequest(code: "system_plugin_protected");

        var filePath = loaded.FilePath;

        // Unload plugin
        if (!_pluginManager.UnloadPlugin(key))
            throw ApiException.BadRequest(code: "plugin_unload_failed");

        // Invalidate registry cache
        _registry.InvalidateCache(key);

        // Delete file (file should not be locked since we load from memory)
        try
        {
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }

            _logger.LogInformation("Plugin deleted: {Key}", key);
            return Ok(new { success = true, message = $"Plugin '{key}' deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete plugin file for {Key}", key);
            return Ok(new
            {
                success = true,
                warning = $"Plugin unloaded, but the file could not be deleted: {ex.Message}. You may need to delete the file manually."
            });
        }
    }

    /// <summary>
    /// Reload a plugin (useful after updating the DLL file)
    /// </summary>
    [HttpPost("{key}/reload")]
    [RequiresPermission(Permissions.PluginsManage)]
    public async Task<IActionResult> ReloadPlugin(string key)
    {
        EnsurePluginsManagementEnabled();
        try
        {
            var reloaded = _pluginManager.ReloadPlugin(key);
            if (reloaded == null)
                throw ApiException.NotFound(code: "plugin_not_found");

            // Invalidate registry cache
            _registry.InvalidateCache(key);

            _logger.LogInformation("Plugin reloaded: {Key}", key);

            // Sync fields for reloaded plugin
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<PluginVersionSyncService>();
                await syncService.SyncAllPluginVersionsAsync();
            }
            catch (Exception syncEx)
            {
                _logger.LogError(syncEx, "Plugin version sync failed after reload of {Key}", key);
            }

            return Ok(new
            {
                success = true,
                key = reloaded.Key,
                displayName = reloaded.Plugin.DisplayName,
                version = reloaded.Version,
                message = $"Plugin '{reloaded.Plugin.DisplayName}' reloaded"
            });
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload plugin {Key}", key);
            throw ApiException.BadRequest(code: "plugin_reload_failed");
        }
    }
}
