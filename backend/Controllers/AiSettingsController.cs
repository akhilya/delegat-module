using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Ai;
using Delegat.Services.Ai;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
[Route("ai")]
public class AiSettingsController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly AiModelConfigService _modelService;
    private readonly DataContext _context;

    public AiSettingsController(
        ICurrentUserService currentUserService,
        AiModelConfigService modelService,
        DataContext context)
    {
        _currentUserService = currentUserService;
        _modelService = modelService;
        _context = context;
    }

    // GET /api/ai/models
    [HttpGet("models")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var models = await _modelService.GetAllAsync(ct);
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
            apiKeyMasked = MaskKey(m.apiKeyEncrypted)
        }));
    }

    // POST /api/ai/models
    [HttpPost("models")]
    public async Task<IActionResult> Create([FromBody] CreateAiModelRequest req, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.ApiUrl)
            || string.IsNullOrWhiteSpace(req.ApiKey) || string.IsNullOrWhiteSpace(req.ModelId))
            return BadRequest("name, apiUrl, apiKey, modelId are required");

        var model = await _modelService.CreateAsync(
            req.Name, req.ApiUrl, req.ApiKey, req.ModelId,
            req.Provider, proxyUrl: req.ProxyUrl, ct: ct);

        return Ok(new { model.Id, model.name, model.modelId, model.isDefault });
    }

    // PUT /api/ai/models/{id}
    [HttpPut("models/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAiModelRequest req, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();

        await _modelService.UpdateAsync(model, req.Name, req.ApiUrl, req.ApiKey, req.ModelId, req.Provider, proxyUrl: req.ProxyUrl, ct: ct);
        return Ok(new { model.Id, model.name });
    }

    // DELETE /api/ai/models/{id}
    [HttpDelete("models/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        await _modelService.DeleteAsync(model, ct);
        return Ok();
    }

    // POST /api/ai/models/{id}/set-default
    [HttpPost("models/{id:int}/set-default")]
    public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        await _modelService.SetDefaultAsync(id, ct);
        return Ok();
    }

    // POST /api/ai/models/{id}/toggle-enabled
    [HttpPost("models/{id:int}/toggle-enabled")]
    public async Task<IActionResult> ToggleEnabled(int id, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        await _modelService.ToggleEnabledAsync(id, ct);
        return Ok();
    }

    // POST /api/ai/models/{id}/test
    [HttpPost("models/{id:int}/test")]
    public async Task<IActionResult> Test(int id, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        var (ok, error) = await _modelService.TestConnectionAsync(model, ct);
        return Ok(new { success = ok, message = ok ? "Подключение работает" : $"Ошибка подключения: {error}" });
    }

    // POST /api/ai/models/discover — fetch available model IDs from a provider API
    [HttpPost("models/discover")]
    public async Task<IActionResult> Discover([FromBody] DiscoverModelsRequest req, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        if (string.IsNullOrWhiteSpace(req.ApiUrl))
            return BadRequest("apiUrl is required");

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
        var storeId = _currentUserService.GetSelectedStoreId();
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
        var storeId = _currentUserService.GetSelectedStoreId();
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

    private async Task EnsureAdminAsync(CancellationToken ct)
    {
        var user = await _currentUserService.GetUserAsync(ct);
        if (user == null || !user.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут управлять AI-моделями");
    }

    private static string MaskKey(string encrypted)
    {
        // Show only last 4 chars of encrypted value to confirm it's set
        if (string.IsNullOrEmpty(encrypted)) return "";
        return encrypted.Length <= 8 ? "****" : $"****{encrypted[^4..]}";
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
    /// <summary>When editing an existing model without re-entering the key — reuse its stored key.</summary>
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
