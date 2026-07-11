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
    private readonly ICurrentUserService _currentUserService;
    private readonly IServiceProvider _serviceProvider;

    public ErrorLogController(
        DataContext context,
        ICurrentUserService currentUserService,
        IServiceProvider serviceProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("/error-logs")]
    public async Task<IActionResult> GetErrorLogs([FromQuery] int limit = 100)
    {
        await EnsureAdminAsync();

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
    public async Task<IActionResult> DeleteErrorLog(int id)
    {
        await EnsureAdminAsync();

        var log = await _context.errorLogs.FindAsync(id);
        if (log == null)
            throw ApiException.NotFound("Лог ошибки не найден");

        _context.errorLogs.Remove(log);
        await _context.SaveChangesAsync();

        return Ok();
    }

    private async Task EnsureAdminAsync()
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут просматривать логи ошибок");
    }
}
