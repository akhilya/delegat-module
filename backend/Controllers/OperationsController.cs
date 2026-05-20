using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Auth;
using PrintO.Models;
using PrintO.Services;

namespace PrintO.Controllers;

[ApiController]
[JwtAuthorize]
public class OperationsController : ControllerBase
{
    private readonly DataContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOperationLogger _operationLogger;

    public OperationsController(DataContext context, ICurrentUserService currentUserService, IOperationLogger operationLogger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _operationLogger = operationLogger;
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
            .Join(
                _context.Users,
                o => o.authorUserId,
                u => u.Id,
                (o, u) => new
                {
                    o.operationId,
                    o.storeId,
                    o.authorUserId,
                    authorUserName = u.UserName,
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

    [HttpPost]
    [Route("/operations/start")]
    public async Task<IActionResult> StartOperation([FromBody] StartOperationInput input, CancellationToken ct)
    {
        var storeId = _currentUserService.GetSelectedStoreId();
        if (storeId == null)
            return BadRequest(new { message = "Не выбран магазин" });

        var userId = _currentUserService.GetUserId() ?? 0;
        var operationId = input.operationId ?? Guid.NewGuid().ToString("N");

        await _operationLogger.StartAsync(operationId, storeId.Value, userId, input.type,
            input.title,
            connectionId: input.connectionId, connectionName: input.connectionName,
            batchId: operationId, batchType: input.batchType, totalItems: input.totalItems);

        return Ok(new { operationId });
    }

    public struct StartOperationInput
    {
        public string? operationId { get; set; }
        public string type { get; set; }
        public string title { get; set; }
        public string? batchType { get; set; }
        public int? connectionId { get; set; }
        public string? connectionName { get; set; }
        public int? totalItems { get; set; }
    }

    /// <summary>
    /// Finish a running operation from the client side (e.g. file uploads).
    /// Sets the final status, summary, and finishedAt timestamp.
    /// </summary>
    [HttpPost]
    [Route("/operations/{operationId}/finish")]
    public async Task<IActionResult> FinishOperation(
        string operationId,
        [FromBody] FinishOperationInput input,
        CancellationToken ct)
    {
        var op = await GetOperationAsync(operationId, ct);
        if (op == null) return NotFound();

        await _operationLogger.FinishAsync(
            operationId,
            op.storeId,
            input.status ?? "completed",
            input.summary);

        return Ok();
    }

    public struct FinishOperationInput
    {
        public string? status { get; set; }
        public string? summary { get; set; }
    }

    /// <summary>
    /// Append a log entry to an operation (client-side operations like file uploads).
    /// </summary>
    [HttpPost]
    [Route("/operations/{operationId}/log")]
    public async Task<IActionResult> AddLog(
        string operationId,
        [FromBody] AddLogInput input,
        CancellationToken ct)
    {
        var op = await GetOperationAsync(operationId, ct);
        if (op == null) return NotFound();

        _context.operationLogEntries.Add(new Models.OperationLogEntry
        {
            operationId = operationId,
            level = input.level ?? "info",
            message = input.message ?? "",
            timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync(ct);
        return Ok();
    }

    public struct AddLogInput
    {
        public string? level { get; set; }
        public string? message { get; set; }
    }

    [HttpGet]
    [Route("/operations/{operationId}/status")]
    public async Task<IActionResult> GetStatus(string operationId, CancellationToken ct)
    {
        var op = await GetOperationAsync(operationId, ct);
        if (op == null) return NotFound();
        return Ok(new { status = op.status });
    }

    [HttpPost]
    [Route("/operations/{operationId}/pause")]
    public async Task<IActionResult> PauseOperation(string operationId, CancellationToken ct)
    {
        var op = await GetOperationAsync(operationId, ct);
        if (op == null) return NotFound();
        if (op.status != "running") return BadRequest(new { message = "Операция не выполняется" });

        op.status = "paused";
        await _context.SaveChangesAsync(ct);

        await _operationLogger.LogAsync(operationId, op.storeId, "warning", "Операция приостановлена");
        await _operationLogger.UpdateStatusAsync(operationId, op.storeId, "paused");

        // Cascade to child operations (e.g. image download)
        foreach (var childId in await GetChildOperationIds(operationId, op.storeId, ct))
        {
            var child = await _context.operations.FirstOrDefaultAsync(o => o.operationId == childId, ct);
            if (child != null && child.status == "running")
            {
                child.status = "paused";
                await _context.SaveChangesAsync(ct);
                await _operationLogger.LogAsync(childId, op.storeId, "warning", "Приостановлено (каскад)");
                await _operationLogger.UpdateStatusAsync(childId, op.storeId, "paused");
            }
        }

        return Ok(new { status = op.status });
    }

    [HttpPost]
    [Route("/operations/{operationId}/resume")]
    public async Task<IActionResult> ResumeOperation(string operationId, CancellationToken ct)
    {
        var op = await GetOperationAsync(operationId, ct);
        if (op == null) return NotFound();
        if (op.status != "paused") return BadRequest(new { message = "Операция не на паузе" });

        op.status = "running";
        await _context.SaveChangesAsync(ct);

        // Re-activate cancelled pending items back to Pending
        if (op.batchId != null)
        {
            if (op.batchType == "import")
            {
                await _context.pendingProductImports
                    .Where(p => p.batchId == op.batchId && p.status == ProductImportStatus.Cancelled)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductImportStatus.Pending), ct);
            }
            else if (op.batchType == "export")
            {
                await _context.pendingProductExports
                    .Where(p => p.batchId == op.batchId && p.status == ProductExportStatus.Cancelled)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Pending), ct);
            }
            else if (op.batchType == "image-download")
            {
                await _context.pendingImageDownloads
                    .Where(p => p.batchId == op.batchId && p.status == ImageDownloadStatus.Cancelled)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Pending), ct);
            }
        }

        await _operationLogger.LogAsync(operationId, op.storeId, "info", "Операция возобновлена");
        await _operationLogger.UpdateStatusAsync(operationId, op.storeId, "running");

        // Cascade to child operations
        foreach (var childId in await GetChildOperationIds(operationId, op.storeId, ct))
        {
            var child = await _context.operations.FirstOrDefaultAsync(o => o.operationId == childId, ct);
            if (child != null && child.status == "paused")
            {
                child.status = "running";
                await _context.SaveChangesAsync(ct);

                // Re-activate cancelled items for child
                if (child.batchType == "image-download")
                {
                    await _context.pendingImageDownloads
                        .Where(p => p.batchId == child.batchId && p.status == ImageDownloadStatus.Cancelled)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Pending), ct);
                }

                await _operationLogger.LogAsync(childId, op.storeId, "info", "Возобновлено (каскад)");
                await _operationLogger.UpdateStatusAsync(childId, op.storeId, "running");
            }
        }

        return Ok(new { status = op.status });
    }

    [HttpPost]
    [Route("/operations/{operationId}/stop")]
    public async Task<IActionResult> StopOperation(string operationId, CancellationToken ct)
    {
        var op = await GetOperationAsync(operationId, ct);
        if (op == null) return NotFound();
        if (op.status != "running" && op.status != "paused")
            return BadRequest(new { message = "Операция уже завершена" });

        // Cancel remaining pending items
        int cancelledCount = 0;
        if (op.batchId != null)
        {
            if (op.batchType == "import")
            {
                cancelledCount = await _context.pendingProductImports
                    .Where(p => p.batchId == op.batchId &&
                        (p.status == ProductImportStatus.Pending || p.status == ProductImportStatus.Cancelled))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductImportStatus.Cancelled), ct);
            }
            else if (op.batchType == "export")
            {
                cancelledCount = await _context.pendingProductExports
                    .Where(p => p.batchId == op.batchId &&
                        (p.status == ProductExportStatus.Pending || p.status == ProductExportStatus.Cancelled))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Cancelled), ct);
            }
            else if (op.batchType == "image-download")
            {
                cancelledCount = await _context.pendingImageDownloads
                    .Where(p => p.batchId == op.batchId &&
                        (p.status == ImageDownloadStatus.Pending || p.status == ImageDownloadStatus.Cancelled))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Cancelled), ct);
            }
        }

        // Calculate final stats
        int completed = 0, failed = 0, total = 0;
        if (op.batchId != null)
        {
            if (op.batchType == "import")
            {
                var items = await _context.pendingProductImports.AsNoTracking()
                    .Where(p => p.batchId == op.batchId).ToListAsync(ct);
                total = items.Count;
                completed = items.Count(p => p.status == ProductImportStatus.Completed);
                failed = items.Count(p => p.status == ProductImportStatus.Failed);
            }
            else if (op.batchType == "export")
            {
                var items = await _context.pendingProductExports.AsNoTracking()
                    .Where(p => p.batchId == op.batchId).ToListAsync(ct);
                total = items.Count;
                completed = items.Count(p => p.status == ProductExportStatus.Completed);
                failed = items.Count(p => p.status == ProductExportStatus.Failed);
            }
            else if (op.batchType == "image-download")
            {
                var items = await _context.pendingImageDownloads.AsNoTracking()
                    .Where(p => p.batchId == op.batchId).ToListAsync(ct);
                total = items.Count;
                completed = items.Count(p => p.status == ImageDownloadStatus.Completed);
                failed = items.Count(p => p.status == ImageDownloadStatus.Failed);
            }
        }

        var summary = $"Остановлено. Выполнено: {completed}, ошибок: {failed}, отменено: {cancelledCount}";
        await _operationLogger.LogAsync(operationId, op.storeId, "warning", summary);
        await _operationLogger.FinishAsync(operationId, op.storeId, "cancelled", summary);

        // Cascade stop to child operations
        foreach (var childId in await GetChildOperationIds(operationId, op.storeId, ct))
        {
            var child = await _context.operations.FirstOrDefaultAsync(o => o.operationId == childId, ct);
            if (child != null && (child.status == "running" || child.status == "paused"))
            {
                int childCancelled = 0;
                if (child.batchType == "image-download")
                {
                    childCancelled = await _context.pendingImageDownloads
                        .Where(p => p.batchId == child.batchId &&
                            (p.status == ImageDownloadStatus.Pending || p.status == ImageDownloadStatus.Cancelled))
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Cancelled), ct);

                    var imgItems = await _context.pendingImageDownloads.AsNoTracking()
                        .Where(p => p.batchId == child.batchId).ToListAsync(ct);
                    var imgCompleted = imgItems.Count(p => p.status == ImageDownloadStatus.Completed);
                    var imgFailed = imgItems.Count(p => p.status == ImageDownloadStatus.Failed);
                    var childSummary = $"Остановлено (каскад). Загружено: {imgCompleted}, ошибок: {imgFailed}, отменено: {childCancelled}";
                    await _operationLogger.LogAsync(childId, op.storeId, "warning", childSummary);
                    await _operationLogger.FinishAsync(childId, op.storeId, "cancelled", childSummary);
                }
            }
        }

        return Ok(new { status = "cancelled" });
    }

    /// <summary>
    /// Find child operations (e.g. "{batchId}-img") and return their IDs.
    /// </summary>
    private async Task<List<string>> GetChildOperationIds(string operationId, int storeId, CancellationToken ct)
    {
        var prefix = operationId + "-";
        return await _context.operations.AsNoTracking()
            .Where(o => o.storeId == storeId && o.operationId.StartsWith(prefix))
            .Select(o => o.operationId)
            .ToListAsync(ct);
    }

    private async Task<Operation?> GetOperationAsync(string operationId, CancellationToken ct)
    {
        var storeId = _currentUserService.GetSelectedStoreId();
        if (storeId == null) return null;
        return await _context.operations
            .FirstOrDefaultAsync(o => o.operationId == operationId && o.storeId == storeId.Value, ct);
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

    [HttpDelete]
    [Route("/operations/bulk")]
    public async Task<IActionResult> DeleteOperationsBulk([FromBody] List<string> operationIds, CancellationToken ct)
    {
        var storeId = _currentUserService.GetSelectedStoreId();
        if (storeId == null)
            return BadRequest(new { message = "Не выбран магазин" });

        if (operationIds == null || operationIds.Count == 0)
            return NoContent();

        // Delete logs first (FK)
        await _context.operationLogEntries
            .Where(e => operationIds.Contains(e.operationId))
            .ExecuteDeleteAsync(ct);

        await _context.operations
            .Where(o => operationIds.Contains(o.operationId) && o.storeId == storeId.Value)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }
}
