using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Auth;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class OperationsController : ControllerBase
{
    private readonly DataContext _context;
    private readonly ICurrentUserService _currentUserService;

    public OperationsController(DataContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    [Route("/operations")]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var storeId = _currentUserService.GetSelectedStoreId();
        if (storeId == null)
            return BadRequest(new { message = "Не выбран магазин" });

        var query = _context.operations
            .AsNoTracking()
            .Where(o => o.storeId == storeId.Value);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(o => o.status == status);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(o => o.createdAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.operationId,
                o.storeId,
                o.authorUserId,
                o.type,
                o.title,
                o.status,
                o.connectionId,
                o.connectionName,
                o.batchId,
                o.batchType,
                o.totalItems,
                o.completedItems,
                o.failedItems,
                o.summary,
                o.createdAt,
                o.finishedAt
            })
            .ToListAsync(ct);

        return Ok(new
        {
            items,
            total,
            page,
            pageSize,
            hasMore = page * pageSize < total
        });
    }

    [HttpGet]
    [Route("/operations/{operationId}/logs")]
    public async Task<IActionResult> Logs(
        string operationId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var storeId = _currentUserService.GetSelectedStoreId();
        if (storeId == null)
            return BadRequest(new { message = "Не выбран магазин" });

        // Verify operation belongs to this store
        var opExists = await _context.operations
            .AsNoTracking()
            .AnyAsync(o => o.operationId == operationId && o.storeId == storeId.Value, ct);

        if (!opExists)
            return NotFound(new { message = "Операция не найдена" });

        var logs = await _context.operationLogEntries
            .AsNoTracking()
            .Where(e => e.operationId == operationId)
            .OrderBy(e => e.timestamp)
            .Skip(skip)
            .Take(take)
            .Select(e => new
            {
                e.level,
                e.message,
                e.timestamp
            })
            .ToListAsync(ct);

        return Ok(new { logs });
    }

    [HttpDelete]
    [Route("/operations/{operationId}")]
    public async Task<IActionResult> DeleteOperation(string operationId, CancellationToken ct)
    {
        var storeId = _currentUserService.GetSelectedStoreId();
        if (storeId == null)
            return BadRequest(new { message = "Не выбран магазин" });

        var op = await _context.operations
            .FirstOrDefaultAsync(o => o.operationId == operationId && o.storeId == storeId.Value, ct);
        if (op == null)
            return NotFound();

        // Delete logs first (FK)
        await _context.operationLogEntries
            .Where(e => e.operationId == operationId)
            .ExecuteDeleteAsync(ct);

        _context.operations.Remove(op);
        await _context.SaveChangesAsync(ct);

        return NoContent();
    }
}
