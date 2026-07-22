using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Services;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class SettingsController : ControllerBase
{
    private readonly IDatabaseDumpService _databaseDumpService;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly IPublicApiSettingsService _publicApiSettings;

    public SettingsController(
        IDatabaseDumpService databaseDumpService,
        ILoggingSettingsService loggingSettings,
        IPublicApiSettingsService publicApiSettings)
    {
        _databaseDumpService = databaseDumpService;
        _loggingSettings = loggingSettings;
        _publicApiSettings = publicApiSettings;
    }

    /// <summary>
    /// Download a full MySQL logical dump (.sql). Administrators only.
    /// </summary>
    [HttpGet]
    [Route("/settings/database/dump")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> DownloadDatabaseDump(CancellationToken ct)
    {
        var handle = await _databaseDumpService.CreateDumpAsync(ct);
        HttpContext.Response.RegisterForDispose(handle);
        return PhysicalFile(handle.FilePath, "application/sql", handle.FileName);
    }

    /// <summary>Current instance logging toggles (admin UI).</summary>
    [HttpGet]
    [Route("/settings/logging")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<LoggingSettingsDto>> GetLoggingSettings(CancellationToken ct)
    {
        return Ok(await _loggingSettings.GetAsync(ct));
    }

    /// <summary>Update instance logging toggles. Takes effect immediately (no restart).</summary>
    [HttpPatch]
    [Route("/settings/logging")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<LoggingSettingsDto>> UpdateLoggingSettings(
        [FromBody] UpdateLoggingSettingsRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "Body required" });

        return Ok(await _loggingSettings.UpdateAsync(request, ct));
    }

    /// <summary>Current Public API enable toggle (admin UI).</summary>
    [HttpGet]
    [Route("/settings/public-api")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<PublicApiSettingsDto>> GetPublicApiSettings(CancellationToken ct)
    {
        return Ok(await _publicApiSettings.GetAsync(ct));
    }

    /// <summary>Enable or disable Public API. Takes effect immediately (no restart).</summary>
    [HttpPatch]
    [Route("/settings/public-api")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<PublicApiSettingsDto>> UpdatePublicApiSettings(
        [FromBody] UpdatePublicApiSettingsRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "Body required" });

        return Ok(await _publicApiSettings.UpdateAsync(request, ct));
    }
}
