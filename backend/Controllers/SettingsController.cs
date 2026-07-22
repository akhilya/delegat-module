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

    public SettingsController(
        IDatabaseDumpService databaseDumpService,
        ILoggingSettingsService loggingSettings)
    {
        _databaseDumpService = databaseDumpService;
        _loggingSettings = loggingSettings;
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
}
