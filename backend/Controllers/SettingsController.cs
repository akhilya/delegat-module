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
    private readonly IDatabaseDumpService _databaseDumpService;

    public SettingsController(
        ICurrentUserService currentUserService,
        IDatabaseDumpService databaseDumpService)
    {
        _currentUserService = currentUserService;
        _databaseDumpService = databaseDumpService;
    }

    /// <summary>
    /// Download a full MySQL logical dump (.sql). Administrators only.
    /// </summary>
    [HttpGet]
    [Route("/settings/database/dump")]
    public async Task<IActionResult> DownloadDatabaseDump(CancellationToken ct)
    {
        await EnsureAdminAsync(ct);

        var handle = await _databaseDumpService.CreateDumpAsync(ct);
        HttpContext.Response.RegisterForDispose(handle);
        return PhysicalFile(handle.FilePath, "application/sql", handle.FileName);
    }

    private async Task EnsureAdminAsync(CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Only administrators can change platform settings");
    }
}
