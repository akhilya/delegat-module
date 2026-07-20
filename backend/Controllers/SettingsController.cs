using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class SettingsController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly AppSettingsService _settingsService;

    public SettingsController(ICurrentUserService currentUserService, AppSettingsService settingsService)
    {
        _currentUserService = currentUserService;
        _settingsService = settingsService;
    }

    [HttpGet]
    [Route("/settings/app")]
    public async Task<IActionResult> GetAppSettings(CancellationToken ct)
    {
        await EnsureAdminAsync(ct);
        return Ok(await _settingsService.GetSettingsAsync(ct));
    }

    [HttpPatch]
    [Route("/settings/app/{key}")]
    public async Task<IActionResult> PatchAppSetting(string key, [FromBody] UpdateAppSettingInput input, CancellationToken ct)
    {
        await EnsureAdminAsync(ct);

        var userId = _currentUserService.GetUserId();
        var setting = await _settingsService.UpdateSettingAsync(key, input.value, userId, ct);
        return Ok(setting);
    }

    public record UpdateAppSettingInput(string? value);

    private async Task EnsureAdminAsync(CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Only administrators can change platform settings");
    }
}
