using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Services;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class SettingsController : ControllerBase
{
    private readonly IDatabaseDumpService _databaseDumpService;

    public SettingsController(IDatabaseDumpService databaseDumpService)
    {
        _databaseDumpService = databaseDumpService;
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
}
