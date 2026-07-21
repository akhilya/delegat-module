using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class ErrorLogController : ControllerBase
{
    private readonly DataContext _context;
    private readonly IServiceProvider _serviceProvider;

    public ErrorLogController(
        DataContext context,
        IServiceProvider serviceProvider)
    {
        _context = context;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("/error-logs")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> GetErrorLogs([FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        var logs = await _context.errorLogs
            .AsNoTracking()
            .OrderByDescending(log => log.createdAt)
            .Take(limit)
            .ToListAsync();

        return Ok(logs.Select(log => log.MapToDTO(_serviceProvider)).ToList());
    }

    [HttpDelete]
    [Route("/error-logs/{id}")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> DeleteErrorLog(int id)
    {
        var log = await _context.errorLogs.FindAsync(id);
        if (log == null)
            throw ApiException.NotFound("Error log entry not found");

        _context.errorLogs.Remove(log);
        await _context.SaveChangesAsync();

        return Ok();
    }
}
