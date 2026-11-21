using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class OperationsController : ControllerBase
{
    private readonly DataContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOperationLogger _operationLogger;
    private readonly IStorageService _storageService;
    private readonly Delegat.Services.Integration.IntegrationCoordinator _integrationCoordinator;

    public OperationsController(
        DataContext context,
        ICurrentUserService currentUserService,
        IOperationLogger operationLogger,
        IStorageService storageService,
        Delegat.Services.Integration.IntegrationCoordinator integrationCoordinator)
    {
        _context = context;
        _currentUserService = currentUserService;
        _operationLogger = operationLogger;
        _storageService = storageService;
        _integrationCoordinator = integrationCoordinator;
    }

    [HttpGet]
    [Route("/operations")]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            return BadRequest(new { message = "Store is not selected", code = "store_not_selected" });

        var query = _context.operations
            .AsNoTracking()
            .Where(o => o.storeId == storeId.Value);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(o => o.status == status);

        var total = await query.CountAsync(ct);

        var pageRows = await query
            .OrderByDescending(o => o.createdAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.operationId,
                o.storeId,
                o.authorUserId,
                o.authorApiKeyId,
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

        var userIds = pageRows.Select(o => o.authorUserId).Distinct().ToList();
        var userNames = await _context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);

        var keyIds = pageRows
            .Where(o => o.authorApiKeyId != null)
            .Select(o => o.authorApiKeyId!.Value)
            .Distinct()
            .ToList();
        var keyRows = await _context.userApiKeys
            .AsNoTracking()
            .Where(k => keyIds.Contains(k.Id))
            .Select(k => new { k.Id, k.name, k.keyPrefix, k.last4 })
            .ToListAsync(ct);
        var keys = keyRows.ToDictionary(k => k.Id);

        var items = pageRows.Select(o =>
        {
            string? authorUserName;
            if (o.authorApiKeyId is int keyId)
            {
                authorUserName = keys.TryGetValue(keyId, out var key)
                    ? OperationAuthor.ApiKey(key.name, key.keyPrefix, key.last4)
                    : "API";
            }
            else
            {
                userNames.TryGetValue(o.authorUserId, out authorUserName);
            }

            return new
            {
                o.operationId,
                o.storeId,
                o.authorUserId,
                o.authorApiKeyId,
                authorUserName,
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
            };
        }).ToList();

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
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            return BadRequest(new { message = "Store is not selected", code = "store_not_selected" });

        // Verify operation belongs to this store
        var opExists = await _context.operations
            .AsNoTracking()
            .AnyAsync(o => o.operationId == operationId && o.storeId == storeId.Value, ct);

        if (!opExists)
            return NotFound(new { message = "Operation not found" });

        var logs = await OperationLogQuery.LatestAsync(
            _context.operationLogEntries.AsNoTracking().Where(e => e.operationId == operationId),
            skip,
            take,
            ct);

        return Ok(new { logs });
    }

    [HttpPost]
    [Route("/operations/start")]
    public async Task<IActionResult> StartOperation([FromBody] StartOperationInput input, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            return BadRequest(new { message = "Store is not selected", code = "store_not_selected" });

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
        return Ok(new
        {
            status = op.status,
            totalItems = op.totalItems,
            completedItems = op.completedItems,
            failedItems = op.failedItems,
            summary = op.summary,
            finishedAt = op.finishedAt,
        });
    }

    [HttpPost]
    [Route("/operations/{operationId}/stop")]
    public async Task<IActionResult> StopOperation(string operationId, CancellationToken ct)
    {
        var op = await GetOperationAsync(operationId, ct);
        if (op == null) return NotFound();
        if (!OperationLifecycle.IsActive(op.status))
            return BadRequest(new { message = "Operation has already finished" });

        // Coordinator-owned operations stop through the durable executor:
        // the flag prevents new stage writes while already-sent stages are
        // still confirmed. The worker projects the authoritative terminal
        // status (cancelled, partial, or failed/unknown) once reconciliation finishes.
        if (await _integrationCoordinator.RequestStopByOperationIdAsync(operationId, ct))
        {
            return Ok(new { status = op.status, stopRequested = true });
        }

        var summary = op.totalItems is > 0
            ? $"Stopped. Completed: {op.completedItems}, errors: {op.failedItems}"
            : "Stopped";
        await _operationLogger.LogAsync(operationId, op.storeId, "warning", summary);
        await _operationLogger.FinishAsync(operationId, op.storeId, "cancelled", summary);

        await CancelQueuedWorkAsync(op, ct);

        foreach (var childId in await GetChildOperationIds(operationId, op.storeId, ct))
        {
            var child = await _context.operations.FirstOrDefaultAsync(o => o.operationId == childId, ct);
            if (child == null || !OperationLifecycle.IsActive(child.status))
                continue;

            await _operationLogger.LogAsync(childId, op.storeId, "warning", "Stopped (cascade)");
            await _operationLogger.FinishAsync(childId, op.storeId, "cancelled", "Stopped (cascade)");
            await CancelQueuedWorkAsync(child, ct);
        }

        return Ok(new { status = "cancelled" });
    }

    /// <summary>
    /// Marks remaining queue rows cancelled. In-flight marketplace HTTP may still finish that one item.
    /// </summary>
    private async Task CancelQueuedWorkAsync(Operation op, CancellationToken ct)
    {
        var batchKey = op.batchId ?? op.operationId;
        if (string.IsNullOrEmpty(batchKey))
            return;

        if (op.batchType == "import")
        {
            await _context.pendingProductImports
                .Where(p => p.batchId == batchKey &&
                    (p.status == ProductImportStatus.Pending
                     || p.status == ProductImportStatus.InProgress))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductImportStatus.Cancelled), ct);
            await _context.pendingCategoryImports
                .Where(p => p.operationId == batchKey &&
                    (p.status == CategoryImportStatus.Pending
                     || p.status == CategoryImportStatus.InProgress
                     || p.status == CategoryImportStatus.Failed))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, CategoryImportStatus.Cancelled), ct);
        }
        else if (op.batchType == "category-import")
        {
            await _context.pendingCategoryImports
                .Where(p => p.operationId == op.operationId &&
                    (p.status == CategoryImportStatus.Pending
                     || p.status == CategoryImportStatus.InProgress
                     || p.status == CategoryImportStatus.Failed))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, CategoryImportStatus.Cancelled), ct);
        }
        else if (op.batchType == "export")
        {
            await _context.pendingProductExports
                .Where(p => p.batchId == batchKey &&
                    (p.status == ProductExportStatus.Pending
                     || p.status == ProductExportStatus.InProgress))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductExportStatus.Cancelled), ct);
        }
        else if (op.batchType == "image-download")
        {
            await _context.pendingImageDownloads
                .Where(p => p.batchId == batchKey &&
                    (p.status == ImageDownloadStatus.Pending
                     || p.status == ImageDownloadStatus.InProgress))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ImageDownloadStatus.Cancelled), ct);
        }
        else if (op.batchType == "folder-job")
        {
            await _context.pendingFolderJobs
                .Where(p => p.operationId == batchKey &&
                    (p.status == FolderJobStatus.Pending
                     || p.status == FolderJobStatus.InProgress
                     || p.status == FolderJobStatus.Failed))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, FolderJobStatus.Cancelled), ct);
        }
        else if (op.batchType == "product-bulk-update")
        {
            await _context.pendingProductBulkUpdates
                .Where(p => p.batchId == batchKey &&
                    (p.status == ProductBulkUpdateStatus.Pending
                     || p.status == ProductBulkUpdateStatus.InProgress))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductBulkUpdateStatus.Cancelled), ct);
        }
        else if (op.batchType == "product-transfer")
        {
            await _context.pendingProductTransfers
                .Where(p => p.batchId == batchKey &&
                    (p.status == ProductTransferStatus.Pending
                     || p.status == ProductTransferStatus.InProgress))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.status, ProductTransferStatus.Cancelled), ct);
        }
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
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null) return null;
        return await _context.operations
            .FirstOrDefaultAsync(o => o.operationId == operationId && o.storeId == storeId.Value, ct);
    }

    [HttpGet]
    [Route("/settings/operations/summary")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> GetOperationsSummary(CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            return BadRequest(new { message = "Store is not selected", code = "store_not_selected" });

        var operations = await _context.operations
            .AsNoTracking()
            .Where(o => o.storeId == storeId.Value)
            .GroupBy(o => o.status)
            .Select(g => new QueueStatusCount(g.Key, g.Count()))
            .ToListAsync(ct);

        return Ok(new
        {
            storeId,
            operations = new
            {
                key = "operations",
                label = "Background processes",
                total = operations.Sum(x => x.count),
                statuses = operations
            },
            queues = new[]
            {
                await BuildExportQueueSummary(storeId.Value, ct),
                await BuildImportQueueSummary(storeId.Value, ct),
                await BuildImageQueueSummary(storeId.Value, ct),
                await BuildTransferQueueSummary(storeId.Value, ct),
                await BuildBulkUpdateQueueSummary(storeId.Value, ct)
            }
        });
    }

    [HttpPost]
    [Route("/settings/operations/clear")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> ClearOperationsQueue([FromBody] ClearOperationsInput input, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            return BadRequest(new { message = "Store is not selected", code = "store_not_selected" });

        var target = (input.target ?? "").Trim().ToLowerInvariant();
        var status = (input.status ?? "inactive").Trim().ToLowerInvariant();

        var deleted = target switch
        {
            "operations" => await ClearOperationsHistory(storeId.Value, status, ct),
            "exports" => await ClearExports(storeId.Value, status, ct),
            "imports" => await ClearImports(storeId.Value, status, ct),
            "images" => await ClearImages(storeId.Value, status, ct),
            "transfers" => await ClearTransfers(storeId.Value, status, ct),
            "bulk-updates" => await ClearBulkUpdates(storeId.Value, status, ct),
            "all-queues" => await ClearExports(storeId.Value, status, ct)
                + await ClearImports(storeId.Value, status, ct)
                + await ClearImages(storeId.Value, status, ct)
                + await ClearTransfers(storeId.Value, status, ct)
                + await ClearBulkUpdates(storeId.Value, status, ct),
            _ => throw ApiException.BadRequest("Unknown queue")
        };

        return Ok(new { deleted });
    }

    public record ClearOperationsInput(string? target, string? status);
    public record QueueStatusCount(string status, int count);
    public record QueueSummary(string key, string label, int total, List<QueueStatusCount> statuses);

    private async Task<QueueSummary> BuildExportQueueSummary(int storeId, CancellationToken ct)
    {
        var grouped = await _context.pendingProductExports
            .AsNoTracking()
            .Where(x => x.storeId == storeId)
            .GroupBy(x => x.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var statuses = grouped
            .Select(g => new QueueStatusCount(g.status.ToString(), g.count))
            .ToList();

        return new QueueSummary("exports", "Product exports", statuses.Sum(x => x.count), statuses);
    }

    private async Task<QueueSummary> BuildImportQueueSummary(int storeId, CancellationToken ct)
    {
        var grouped = await _context.pendingProductImports
            .AsNoTracking()
            .Where(x => x.storeId == storeId)
            .GroupBy(x => x.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var statuses = grouped
            .Select(g => new QueueStatusCount(g.status.ToString(), g.count))
            .ToList();

        return new QueueSummary("imports", "Product imports", statuses.Sum(x => x.count), statuses);
    }

    private async Task<QueueSummary> BuildImageQueueSummary(int storeId, CancellationToken ct)
    {
        var grouped = await _context.pendingImageDownloads
            .AsNoTracking()
            .Where(x => x.storeId == storeId)
            .GroupBy(x => x.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var statuses = grouped
            .Select(g => new QueueStatusCount(g.status.ToString(), g.count))
            .ToList();

        return new QueueSummary("images", "Image downloads", statuses.Sum(x => x.count), statuses);
    }

    private async Task<QueueSummary> BuildTransferQueueSummary(int storeId, CancellationToken ct)
    {
        var grouped = await _context.pendingProductTransfers
            .AsNoTracking()
            .Where(x => x.sourceStoreId == storeId || x.targetStoreId == storeId)
            .GroupBy(x => x.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var statuses = grouped
            .Select(g => new QueueStatusCount(g.status.ToString(), g.count))
            .ToList();

        return new QueueSummary("transfers", "Product transfers", statuses.Sum(x => x.count), statuses);
    }

    private async Task<QueueSummary> BuildBulkUpdateQueueSummary(int storeId, CancellationToken ct)
    {
        var grouped = await _context.pendingProductBulkUpdates
            .AsNoTracking()
            .Where(x => x.storeId == storeId)
            .GroupBy(x => x.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var statuses = grouped
            .Select(g => new QueueStatusCount(g.status.ToString(), g.count))
            .ToList();

        return new QueueSummary("bulk-updates", "Product bulk updates", statuses.Sum(x => x.count), statuses);
    }

    private static bool IsInactiveStatus(string status) =>
        status is "inactive" or "finished" or "completed-failed-cancelled";

    private static IQueryable<Operation> FilterOperationsByStatus(IQueryable<Operation> query, string status) =>
        status switch
        {
            "all" => query,
            "running" => query.Where(x => x.status == "running"),
            "paused" => query.Where(x => x.status == "paused"),
            "completed" => query.Where(x => x.status == "completed"),
            "failed" => query.Where(x => x.status == "failed"),
            "cancelled" => query.Where(x => x.status == "cancelled"),
            _ when IsInactiveStatus(status) => query.Where(x => x.status == "completed" || x.status == "failed" || x.status == "cancelled"),
            _ => throw ApiException.BadRequest("Unknown cleanup status")
        };

    private static IQueryable<PendingProductExport> FilterExportsByStatus(IQueryable<PendingProductExport> query, string status) =>
        status switch
        {
            "all" => query,
            "pending" => query.Where(x => x.status == ProductExportStatus.Pending),
            "in-progress" => query.Where(x => x.status == ProductExportStatus.InProgress),
            "completed" => query.Where(x => x.status == ProductExportStatus.Completed),
            "failed" => query.Where(x => x.status == ProductExportStatus.Failed),
            "cancelled" => query.Where(x => x.status == ProductExportStatus.Cancelled),
            _ when IsInactiveStatus(status) => query.Where(x => x.status == ProductExportStatus.Completed || x.status == ProductExportStatus.Failed || x.status == ProductExportStatus.Cancelled),
            _ => throw ApiException.BadRequest("Unknown cleanup status")
        };

    private static IQueryable<PendingProductImport> FilterImportsByStatus(IQueryable<PendingProductImport> query, string status) =>
        status switch
        {
            "all" => query,
            "pending" => query.Where(x => x.status == ProductImportStatus.Pending),
            "in-progress" => query.Where(x => x.status == ProductImportStatus.InProgress),
            "completed" => query.Where(x => x.status == ProductImportStatus.Completed),
            "failed" => query.Where(x => x.status == ProductImportStatus.Failed),
            "cancelled" => query.Where(x => x.status == ProductImportStatus.Cancelled),
            _ when IsInactiveStatus(status) => query.Where(x => x.status == ProductImportStatus.Completed || x.status == ProductImportStatus.Failed || x.status == ProductImportStatus.Cancelled),
            _ => throw ApiException.BadRequest("Unknown cleanup status")
        };

    private static IQueryable<PendingImageDownload> FilterImagesByStatus(IQueryable<PendingImageDownload> query, string status) =>
        status switch
        {
            "all" => query,
            "pending" => query.Where(x => x.status == ImageDownloadStatus.Pending),
            "in-progress" => query.Where(x => x.status == ImageDownloadStatus.InProgress),
            "completed" => query.Where(x => x.status == ImageDownloadStatus.Completed),
            "failed" => query.Where(x => x.status == ImageDownloadStatus.Failed),
            "cancelled" => query.Where(x => x.status == ImageDownloadStatus.Cancelled),
            _ when IsInactiveStatus(status) => query.Where(x => x.status == ImageDownloadStatus.Completed || x.status == ImageDownloadStatus.Failed || x.status == ImageDownloadStatus.Cancelled),
            _ => throw ApiException.BadRequest("Unknown cleanup status")
        };

    private static IQueryable<PendingProductTransfer> FilterTransfersByStatus(IQueryable<PendingProductTransfer> query, string status) =>
        status switch
        {
            "all" => query,
            "pending" => query.Where(x => x.status == ProductTransferStatus.Pending),
            "in-progress" => query.Where(x => x.status == ProductTransferStatus.InProgress),
            "completed" => query.Where(x => x.status == ProductTransferStatus.Completed),
            "failed" => query.Where(x => x.status == ProductTransferStatus.Failed),
            "cancelled" => query.Where(x => x.status == ProductTransferStatus.Cancelled),
            "partial" => query.Where(x => x.status == ProductTransferStatus.Partial),
            _ when IsInactiveStatus(status) => query.Where(x =>
                x.status == ProductTransferStatus.Completed ||
                x.status == ProductTransferStatus.Failed ||
                x.status == ProductTransferStatus.Cancelled ||
                x.status == ProductTransferStatus.Partial),
            _ => throw ApiException.BadRequest("Unknown cleanup status")
        };

    private static IQueryable<PendingProductBulkUpdate> FilterBulkUpdatesByStatus(
        IQueryable<PendingProductBulkUpdate> query, string status) =>
        status switch
        {
            "all" => query,
            "pending" => query.Where(x => x.status == ProductBulkUpdateStatus.Pending),
            "in-progress" => query.Where(x => x.status == ProductBulkUpdateStatus.InProgress),
            "completed" => query.Where(x => x.status == ProductBulkUpdateStatus.Completed),
            "failed" => query.Where(x => x.status == ProductBulkUpdateStatus.Failed),
            "cancelled" => query.Where(x => x.status == ProductBulkUpdateStatus.Cancelled),
            "partial" => query.Where(x => x.status == ProductBulkUpdateStatus.Partial),
            _ when IsInactiveStatus(status) => query.Where(x =>
                x.status == ProductBulkUpdateStatus.Completed ||
                x.status == ProductBulkUpdateStatus.Failed ||
                x.status == ProductBulkUpdateStatus.Cancelled ||
                x.status == ProductBulkUpdateStatus.Partial),
            _ => throw ApiException.BadRequest("Unknown cleanup status")
        };

    private async Task<int> ClearOperationsHistory(int storeId, string status, CancellationToken ct)
    {
        var operationIds = await FilterOperationsByStatus(
                _context.operations.AsNoTracking().Where(x => x.storeId == storeId),
                status)
            .Select(x => x.operationId)
            .ToListAsync(ct);

        // Projections of unresolved durable commands survive history clears.
        var blocked = await _integrationCoordinator.GetUnresolvedOperationIdsAsync(operationIds, ct);
        if (blocked.Count > 0)
            operationIds = operationIds.Where(id => !blocked.Contains(id)).ToList();

        if (operationIds.Count == 0) return 0;

        await _context.operationLogEntries
            .Where(x => operationIds.Contains(x.operationId))
            .ExecuteDeleteAsync(ct);

        return await _context.operations
            .Where(x => operationIds.Contains(x.operationId) && x.storeId == storeId)
            .ExecuteDeleteAsync(ct);
    }

    private Task<int> ClearExports(int storeId, string status, CancellationToken ct) =>
        FilterExportsByStatus(_context.pendingProductExports.Where(x => x.storeId == storeId), status)
            .ExecuteDeleteAsync(ct);

    private Task<int> ClearImports(int storeId, string status, CancellationToken ct) =>
        FilterImportsByStatus(_context.pendingProductImports.Where(x => x.storeId == storeId), status)
            .ExecuteDeleteAsync(ct);

    private Task<int> ClearImages(int storeId, string status, CancellationToken ct) =>
        FilterImagesByStatus(_context.pendingImageDownloads.Where(x => x.storeId == storeId), status)
            .ExecuteDeleteAsync(ct);

    private Task<int> ClearTransfers(int storeId, string status, CancellationToken ct) =>
        FilterTransfersByStatus(_context.pendingProductTransfers.Where(x => x.sourceStoreId == storeId || x.targetStoreId == storeId), status)
            .ExecuteDeleteAsync(ct);

    private Task<int> ClearBulkUpdates(int storeId, string status, CancellationToken ct) =>
        FilterBulkUpdatesByStatus(_context.pendingProductBulkUpdates.Where(x => x.storeId == storeId), status)
            .ExecuteDeleteAsync(ct);

    [HttpGet]
    [Route("/operations/{operationId}/artifact")]
    public async Task DownloadArtifact(string operationId, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");

        var op = await _context.operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.operationId == operationId && o.storeId == storeId.Value, ct);
        if (op == null)
            throw ApiException.NotFound("Operation not found");

        var job = await _context.pendingFolderJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.operationId == operationId && j.storeId == storeId.Value, ct);

        if (job == null
            || job.jobType != FolderJobType.Archive
            || job.status != FolderJobStatus.Completed
            || string.IsNullOrEmpty(job.resultObjectKey))
        {
            throw ApiException.NotFound("Archive artifact not available", code: "artifact_not_found");
        }

        if (!await _storageService.ExistsAsync(job.resultObjectKey))
            throw ApiException.NotFound("Archive artifact not found in storage", code: "artifact_not_found");

        var fileName = job.resultFileName ?? "archive.zip";
        StorageDownloadHttp.Prepare(Response, "application/zip", fileName, inline: false, job.resultBytes);
        await _storageService.StreamToAsync(job.resultObjectKey, Response.Body, ct);
    }

    [HttpDelete]
    [Route("/operations/{operationId}")]
    public async Task<IActionResult> DeleteOperation(string operationId, CancellationToken ct)
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            return BadRequest(new { message = "Store is not selected", code = "store_not_selected" });

        var op = await _context.operations
            .FirstOrDefaultAsync(o => o.operationId == operationId && o.storeId == storeId.Value, ct);
        if (op == null)
            return NotFound();

        // An unresolved durable command owns its projection; deleting it would
        // hide live effect evidence from operators.
        if (await _integrationCoordinator.IsUnresolvedCommandOperationAsync(operationId, ct))
            return Conflict(new
            {
                message = "Operation is backed by an unresolved integration command and cannot be deleted",
                code = "operation_command_unresolved"
            });

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
        var storeId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (storeId == null)
            return BadRequest(new { message = "Store is not selected", code = "store_not_selected" });

        if (operationIds == null || operationIds.Count == 0)
            return NoContent();

        // Projections of unresolved durable commands survive bulk history deletes.
        var blocked = await _integrationCoordinator.GetUnresolvedOperationIdsAsync(operationIds, ct);
        var deletable = operationIds.Where(id => !blocked.Contains(id)).ToList();
        if (deletable.Count == 0)
            return NoContent();

        // Delete logs first (FK)
        await _context.operationLogEntries
            .Where(e => deletable.Contains(e.operationId))
            .ExecuteDeleteAsync(ct);

        await _context.operations
            .Where(o => deletable.Contains(o.operationId) && o.storeId == storeId.Value)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }
}
