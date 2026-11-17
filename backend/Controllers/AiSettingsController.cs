using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("ai")]
public class AiSettingsController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly AiModelConfigService _modelService;
    private readonly DataContext _context;
    private readonly AiMcpServerService _mcpServers;
    private readonly McpHttpClient _mcpHttp;

    public AiSettingsController(
        ICurrentUserService currentUserService,
        AiModelConfigService modelService,
        AiMcpServerService mcpServers,
        McpHttpClient mcpHttp,
        DataContext context)
    {
        _currentUserService = currentUserService;
        _modelService = modelService;
        _mcpServers = mcpServers;
        _mcpHttp = mcpHttp;
        _context = context;
    }

    /// <summary>Lists local and platform-provided models; provided models are read-only and have no key mask.</summary>
    [HttpGet("models")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var models = await _modelService.GetPickerModelsAsync(ct);
        return Ok(models.Select(m => new
        {
            m.Id,
            m.name,
            m.apiUrl,
            m.modelId,
            m.provider,
            m.maxTokens,
            m.isDefault,
            m.isEnabled,
            m.createdAt,
            m.proxyUrl,
            m.isPlatformHosted,
            apiKeyMasked = m.isPlatformHosted ? null : MaskKey(m.apiKeyEncrypted)
        }));
    }

    /// <summary>
    /// Enabled models for UI pickers (mapping llmGenerate, etc.) - any authenticated user.
    /// </summary>
    [HttpGet("models/picker")]
    public async Task<IActionResult> GetPicker(CancellationToken ct)
    {
        var user = await _currentUserService.GetUserAsync(ct);
        if (user == null)
            throw ApiException.Unauthorized();

        var models = await _modelService.GetPickerModelsAsync(ct);
        return Ok(models
            .Where(m => m.isEnabled)
            .OrderBy(m => m.isPlatformHosted)
            .ThenByDescending(m => m.isDefault)
            .ThenBy(m => m.name)
            .Select(m => new
            {
                id = m.Id,
                name = m.name,
                modelId = m.modelId,
                isDefault = m.isDefault,
                isPlatformHosted = m.isPlatformHosted,
            }));
    }

    /// <summary>Returns the monthly usage percentage and availability, without raw token counts or period dates.</summary>
    [HttpGet("hosted-ai")]
    [RequiresPermission(Permissions.AiUse, requireStoreMembership: true)]
    public Task<IActionResult> GetHostedAiStatus(CancellationToken ct) => ReadHostedAiStatusAsync(ct);

    /// <summary>Returns the same percentage-only usage status for instance administrators.</summary>
    [HttpGet("hosted-ai/admin")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public Task<IActionResult> GetHostedAiAdminStatus(CancellationToken ct) => ReadHostedAiStatusAsync(ct);

    private async Task<IActionResult> ReadHostedAiStatusAsync(CancellationToken ct)
    {
        var config = await _modelService.GetPlatformHostedConfigAsync(ct);
        var quota = Math.Max(0, config?.QuotaTokens ?? 0);
        var used = Math.Max(0, config?.UsedTokens ?? 0);
        var usagePercent = quota > 0
            ? Math.Round(Math.Clamp((double)used / quota * 100, 0, 100), 1, MidpointRounding.AwayFromZero)
            : 0;
        return Ok(new
        {
            configured = config?.Configured ?? false,
            entitled = config?.Entitled ?? false,
            available = config?.Available ?? false,
            usagePercent,
            quotaExhausted = quota > 0 && used >= quota,
            usageUnavailable = config?.UsageUnavailable ?? false,
        });
    }

    // POST /api/ai/models
    [HttpPost("models")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> Create([FromBody] CreateAiModelRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.ApiUrl)
            || string.IsNullOrWhiteSpace(req.ApiKey) || string.IsNullOrWhiteSpace(req.ModelId))
            return BadRequest("name, apiUrl, apiKey, modelId are required");

        if (!UrlSafety.TryValidateHttpUrl(req.ApiUrl, out _, out var apiUrlError, allowPrivateHosts: true))
            return BadRequest($"apiUrl: {apiUrlError}");
        if (!UrlSafety.TryValidateProxyUrl(req.ProxyUrl, out var proxyError, allowPrivateHosts: true))
            return BadRequest($"proxyUrl: {proxyError}");

        var model = await _modelService.CreateAsync(
            req.Name, req.ApiUrl, req.ApiKey, req.ModelId,
            req.Provider, proxyUrl: req.ProxyUrl, ct: ct);

        return Ok(new { model.Id, model.name, model.modelId, model.isDefault });
    }

    // PUT /api/ai/models/{id}
    [HttpPut("models/{id:int}")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAiModelRequest req, CancellationToken ct)
    {
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();

        if (req.ApiUrl != null && !UrlSafety.TryValidateHttpUrl(req.ApiUrl, out _, out var apiUrlError, allowPrivateHosts: true))
            return BadRequest($"apiUrl: {apiUrlError}");
        if (req.ProxyUrl != null && !UrlSafety.TryValidateProxyUrl(req.ProxyUrl, out var proxyError, allowPrivateHosts: true))
            return BadRequest($"proxyUrl: {proxyError}");

        await _modelService.UpdateAsync(model, req.Name, req.ApiUrl, req.ApiKey, req.ModelId, req.Provider, proxyUrl: req.ProxyUrl, ct: ct);
        return Ok(new { model.Id, model.name });
    }

    // DELETE /api/ai/models/{id}
    [HttpDelete("models/{id:int}")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        await _modelService.DeleteAsync(model, ct);
        return Ok();
    }

    // POST /api/ai/models/{id}/set-default
    [HttpPost("models/{id:int}/set-default")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
    {
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        await _modelService.SetDefaultAsync(id, ct);
        return Ok();
    }

    // POST /api/ai/models/{id}/toggle-enabled
    [HttpPost("models/{id:int}/toggle-enabled")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> ToggleEnabled(int id, CancellationToken ct)
    {
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        await _modelService.ToggleEnabledAsync(id, ct);
        return Ok();
    }

    // POST /api/ai/models/{id}/test
    [HttpPost("models/{id:int}/test")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> Test(int id, CancellationToken ct)
    {
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        var (ok, error) = await _modelService.TestConnectionAsync(model, ct);
        return Ok(new { success = ok, message = ok ? "Connection is working" : $"Connection error: {error}" });
    }

    // POST /api/ai/models/discover - fetch available model IDs from a provider API
    [HttpPost("models/discover")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> Discover([FromBody] DiscoverModelsRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ApiUrl))
            return BadRequest("apiUrl is required");

        if (!UrlSafety.TryValidateHttpUrl(req.ApiUrl, out _, out var apiUrlError, allowPrivateHosts: true))
            return BadRequest($"apiUrl: {apiUrlError}");
        if (!UrlSafety.TryValidateProxyUrl(req.ProxyUrl, out var proxyError, allowPrivateHosts: true))
            return BadRequest($"proxyUrl: {proxyError}");

        // If editing an existing model without re-entering the key, reuse the stored one
        var apiKey = req.ApiKey;
        if (string.IsNullOrEmpty(apiKey) && req.ModelConfigId.HasValue)
        {
            var existing = await _modelService.GetByIdAsync(req.ModelConfigId.Value, ct);
            if (existing != null)
                apiKey = _modelService.DecryptForUse(existing).apiKeyEncrypted; // DecryptForUse stores plaintext here
        }

        try
        {
            var ids = await _modelService.DiscoverModelsAsync(req.ApiUrl, apiKey, req.ResolveProvider(), req.ProxyUrl, ct);
            return Ok(new { models = ids });
        }
        catch (Exception ex)
        {
            return Ok(new { models = new List<string>(), error = ex.Message });
        }
    }

    // GET /api/ai/store-settings
    [HttpGet("store-settings")]
    public async Task<IActionResult> GetStoreSettings(CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (!storeId.HasValue) return Unauthorized();

        var settings = await _context.aiStoreSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId.Value, ct);

        return Ok(new
        {
            storeDescription = settings?.StoreDescription ?? "",
            communicationStyle = settings?.CommunicationStyle ?? "professional",
            responseBrevity = settings?.ResponseBrevity ?? "balanced",
            emojiUsage = settings?.EmojiUsage ?? "none",
        });
    }

    // PUT /api/ai/store-settings
    [HttpPut("store-settings")]
    public async Task<IActionResult> SaveStoreSettings([FromBody] SaveStoreSettingsRequest req, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (!storeId.HasValue) return Unauthorized();

        var settings = await _context.aiStoreSettings
            .FirstOrDefaultAsync(s => s.StoreId == storeId.Value, ct);

        if (settings == null)
        {
            settings = new AiStoreSettings { StoreId = storeId.Value };
            _context.aiStoreSettings.Add(settings);
        }

        settings.StoreDescription = req.StoreDescription ?? "";
        settings.CommunicationStyle = req.CommunicationStyle ?? "professional";
        settings.ResponseBrevity = req.ResponseBrevity ?? "balanced";
        settings.EmojiUsage = req.EmojiUsage ?? "none";
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpGet("mcp-servers")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> GetMcpServers(CancellationToken ct)
    {
        var rows = await _mcpServers.GetAllAsync(ct);
        return Ok(rows.Select(s => new
        {
            s.Id,
            s.name,
            s.url,
            s.isEnabled,
            s.createdAt,
            hasApiKey = !string.IsNullOrEmpty(s.apiKeyEncrypted),
        }));
    }

    [HttpPost("mcp-servers")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> CreateMcpServer([FromBody] SaveAiMcpServerRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Url))
            return BadRequest("name and url are required");
        if (!UrlSafety.TryValidateHttpUrl(req.Url, out _, out var urlError, allowPrivateHosts: true))
            return BadRequest($"url: {urlError}");
        try
        {
            var row = await _mcpServers.CreateAsync(req.Name, req.Url, req.ApiKey, ct);
            return Ok(new { row.Id, row.name, row.url, row.isEnabled });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("mcp-servers/{id:int}")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> UpdateMcpServer(int id, [FromBody] SaveAiMcpServerRequest req, CancellationToken ct)
    {
        var row = await _mcpServers.GetByIdAsync(id, ct);
        if (row == null) return NotFound();
        if (req.Url != null && !UrlSafety.TryValidateHttpUrl(req.Url, out _, out var urlError, allowPrivateHosts: true))
            return BadRequest($"url: {urlError}");
        try
        {
            await _mcpServers.UpdateAsync(row, req.Name, req.Url, req.ApiKey, ct);
            return Ok(new { row.Id, row.name });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("mcp-servers/{id:int}")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> DeleteMcpServer(int id, CancellationToken ct)
    {
        var row = await _mcpServers.GetByIdAsync(id, ct);
        if (row == null) return NotFound();
        await _mcpServers.DeleteAsync(row, ct);
        return Ok();
    }

    [HttpPost("mcp-servers/{id:int}/toggle-enabled")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> ToggleMcpServerEnabled(int id, CancellationToken ct)
    {
        var row = await _mcpServers.GetByIdAsync(id, ct);
        if (row == null) return NotFound();
        await _mcpServers.ToggleEnabledAsync(id, ct);
        return Ok();
    }

    [HttpPost("mcp-servers/{id:int}/test")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> TestMcpServer(int id, CancellationToken ct)
    {
        var row = await _mcpServers.GetByIdAsync(id, ct);
        if (row == null) return NotFound();
        try
        {
            var tools = await _mcpHttp.ListToolsAsync(row.Id, row.url, _mcpServers.DecryptApiKey(row), ct);
            return Ok(new
            {
                success = true,
                message = tools.Count == 1 ? "1 tool" : $"{tools.Count} tools",
                toolCount = tools.Count,
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message, toolCount = 0 });
        }
    }

    private static string MaskKey(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        return "••••••••";
    }
}

public class SaveStoreSettingsRequest
{
    public string? StoreDescription { get; set; }
    public string? CommunicationStyle { get; set; }
    public string? ResponseBrevity { get; set; }
    public string? EmojiUsage { get; set; }
}

public class CreateAiModelRequest
{
    public string Name { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ModelId { get; set; } = "";
    public AiModelProvider Provider { get; set; } = AiModelProvider.Anthropic;
    public string? ProxyUrl { get; set; }
}

public class UpdateAiModelRequest
{
    public string? Name { get; set; }
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ModelId { get; set; }
    public AiModelProvider? Provider { get; set; }
    public string? ProxyUrl { get; set; }
}

public class DiscoverModelsRequest
{
    public string ApiUrl { get; set; } = "";
    public string? ApiKey { get; set; }
    public string? ProxyUrl { get; set; }
    /// <summary>Accepts number (0/1), string ("0"/"1"/"Anthropic"/"OpenAiCompatible") or null.</summary>
    public System.Text.Json.JsonElement? Provider { get; set; }
    /// <summary>When editing an existing model without re-entering the key - reuse its stored key.</summary>
    public int? ModelConfigId { get; set; }

    public AiModelProvider ResolveProvider()
    {
        if (Provider is not { } el) return AiModelProvider.OpenAiCompatible;
        switch (el.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Number when el.TryGetInt32(out var n):
                return (AiModelProvider)n;
            case System.Text.Json.JsonValueKind.String:
                var s = el.GetString();
                if (int.TryParse(s, out var num)) return (AiModelProvider)num;
                if (Enum.TryParse<AiModelProvider>(s, true, out var parsed)) return parsed;
                break;
        }
        return AiModelProvider.OpenAiCompatible;
    }
}

public class SaveAiMcpServerRequest
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? ApiKey { get; set; }
}
