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
    public async Task<IActionResult> GetErrorLogs(
        [FromQuery] int limit = 100,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        limit = Math.Clamp(limit, 1, 500);
        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        IQueryable<ErrorLog> baseQuery = _context.errorLogs.AsNoTracking();
        IOrderedQueryable<ErrorLog> ordered = (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "method" => asc ? baseQuery.OrderBy(l => l.method) : baseQuery.OrderByDescending(l => l.method),
            "message" or "error" or "path" => asc
                ? baseQuery.OrderBy(l => l.message)
                : baseQuery.OrderByDescending(l => l.message),
            "username" or "user" => asc
                ? baseQuery.OrderBy(l => l.userName)
                : baseQuery.OrderByDescending(l => l.userName),
            "statuscode" or "status" or "code" => asc
                ? baseQuery.OrderBy(l => l.statusCode)
                : baseQuery.OrderByDescending(l => l.statusCode),
            _ => asc
                ? baseQuery.OrderBy(l => l.createdAt)
                : baseQuery.OrderByDescending(l => l.createdAt),
        };
        // Stable tie-break so the same sort always yields the same sequence.
        var logs = await ordered.ThenByDescending(l => l.Id).Take(limit).ToListAsync();
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

    /// <summary>Delete selected error log rows (max 200).</summary>
    [HttpPost]
    [Route("/error-logs/delete")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> DeleteMany([FromBody] DeleteLogIdsRequest? body, CancellationToken ct = default)
    {
        var ids = (body?.Ids ?? [])
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToList();
        if (ids.Count == 0)
            return Ok(new { deleted = 0 });

        var deleted = await _context.errorLogs
            .Where(l => ids.Contains(l.Id))
            .ExecuteDeleteAsync(ct);

        return Ok(new { deleted });
    }
}
