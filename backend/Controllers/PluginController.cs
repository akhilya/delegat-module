using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
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
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<PluginController> _logger;

    public PluginController(
        PluginManager pluginManager,
        PluginAdapterRegistry registry,
        IServiceScopeFactory scopeFactory,
        ICurrentUserService currentUserService,
        ILogger<PluginController> logger)
    {
        _pluginManager = pluginManager;
        _registry = registry;
        _scopeFactory = scopeFactory;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <summary>
    /// Get list of all installed plugins
    /// </summary>
    [HttpGet]
    public IActionResult GetPlugins()
    {
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
        var loaded = _pluginManager.GetLoadedPlugin(key);
        if (loaded == null)
            throw ApiException.NotFound($"Плагин '{key}' не найден");

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
    public async Task<IActionResult> UploadPlugin(IFormFile file)
    {
        await EnsureAdminAsync();

        if (file == null || file.Length == 0)
            throw ApiException.BadRequest("Файл не предоставлен");

        var safeFileName = FileNameSafety.Sanitize(file.FileName, "plugin.dll");
        if (!safeFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest("Разрешены только файлы .dll");

        // Validate file size (max 50MB)
        if (file.Length > 50 * 1024 * 1024)
            throw ApiException.BadRequest("Файл слишком большой. Максимальный размер — 50 МБ");

        var pluginsDir = _pluginManager.PluginsDirectory;
        var destPath = Path.GetFullPath(Path.Combine(pluginsDir, safeFileName));
        if (!destPath.StartsWith(Path.GetFullPath(pluginsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !destPath.Equals(Path.GetFullPath(pluginsDir), StringComparison.OrdinalIgnoreCase))
            throw ApiException.BadRequest("Некорректное имя файла плагина");

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
                message = $"Плагин '{loaded.Plugin.DisplayName}' успешно установлен"
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
            throw ApiException.BadRequest($"Не удалось загрузить плагин: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a plugin
    /// </summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> DeletePlugin(string key)
    {
        await EnsureAdminAsync();

        var loaded = _pluginManager.GetLoadedPlugin(key);
        if (loaded == null)
            throw ApiException.NotFound($"Плагин '{key}' не найден");

        if (loaded.Plugin.IsSystem)
            throw ApiException.BadRequest("Системный плагин не может быть удалён");

        var filePath = loaded.FilePath;

        // Unload plugin
        if (!_pluginManager.UnloadPlugin(key))
            throw ApiException.BadRequest($"Не удалось выгрузить плагин '{key}'");

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
            return Ok(new { success = true, message = $"Плагин '{key}' удалён" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete plugin file for {Key}", key);
            return Ok(new
            {
                success = true,
                warning = $"Плагин выгружен, но удаление файла не удалось: {ex.Message}. Возможно, вам придётся удалить файл вручную."
            });
        }
    }

    /// <summary>
    /// Reload a plugin (useful after updating the DLL file)
    /// </summary>
    [HttpPost("{key}/reload")]
    public async Task<IActionResult> ReloadPlugin(string key)
    {
        await EnsureAdminAsync();

        try
        {
            var reloaded = _pluginManager.ReloadPlugin(key);
            if (reloaded == null)
                throw ApiException.NotFound($"Плагин '{key}' не найден");

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
                message = $"Плагин '{reloaded.Plugin.DisplayName}' перезагружен"
            });
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload plugin {Key}", key);
            throw ApiException.BadRequest($"Не удалось перезагрузить плагин: {ex.Message}");
        }
    }

    private async Task EnsureAdminAsync()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null || !user.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут управлять плагинами");
    }
}
