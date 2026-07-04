using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Ai;
using Delegat.Services.Ai;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class AiSettingsController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly AiModelConfigService _modelService;

    public AiSettingsController(
        ICurrentUserService currentUserService,
        AiModelConfigService modelService)
    {
        _currentUserService = currentUserService;
        _modelService = modelService;
    }

    // GET /api/ai/models
    [HttpGet("api/ai/models")]
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
            m.createdAt,
            apiKeyMasked = MaskKey(m.apiKeyEncrypted)
        }));
    }

    // POST /api/ai/models
    [HttpPost("api/ai/models")]
    public async Task<IActionResult> Create([FromBody] CreateAiModelRequest req, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.ApiUrl)
            || string.IsNullOrWhiteSpace(req.ApiKey) || string.IsNullOrWhiteSpace(req.ModelId))
            return BadRequest("name, apiUrl, apiKey, modelId are required");

        var model = await _modelService.CreateAsync(
            req.Name, req.ApiUrl, req.ApiKey, req.ModelId,
            req.Provider, req.MaxTokens ?? 8192, ct);

        return Ok(new { model.Id, model.name, model.modelId, model.isDefault });
    }

    // PUT /api/ai/models/{id}
    [HttpPut("api/ai/models/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAiModelRequest req, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();

        await _modelService.UpdateAsync(model, req.Name, req.ApiUrl, req.ApiKey, req.ModelId, req.Provider, req.MaxTokens, ct);
        return Ok(new { model.Id, model.name });
    }

    // DELETE /api/ai/models/{id}
    [HttpDelete("api/ai/models/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        await _modelService.DeleteAsync(model, ct);
        return Ok();
    }

    // POST /api/ai/models/{id}/set-default
    [HttpPost("api/ai/models/{id:int}/set-default")]
    public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        await _modelService.SetDefaultAsync(id, ct);
        return Ok();
    }

    // POST /api/ai/models/{id}/test
    [HttpPost("api/ai/models/{id:int}/test")]
    public async Task<IActionResult> Test(int id, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        var model = await _modelService.GetByIdAsync(id, ct);
        if (model == null) return NotFound();
        var ok = await _modelService.TestConnectionAsync(model, ct);
        return Ok(new { success = ok, message = ok ? "Подключение работает" : "Ошибка подключения" });
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

public class CreateAiModelRequest
{
    public string Name { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ModelId { get; set; } = "";
    public AiModelProvider Provider { get; set; } = AiModelProvider.Anthropic;
    public int? MaxTokens { get; set; }
}

public class UpdateAiModelRequest
{
    public string? Name { get; set; }
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ModelId { get; set; }
    public AiModelProvider? Provider { get; set; }
    public int? MaxTokens { get; set; }
}
