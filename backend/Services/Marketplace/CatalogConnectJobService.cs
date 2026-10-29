using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

public sealed class CatalogConnectJobDto
{
    public string OperationId { get; set; } = "";
    public int ConnectionId { get; set; }
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Mode { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int QueuedCount { get; set; }
    public string? ImportBatchId { get; set; }
    public CatalogMatchPreviewResult? Preview { get; set; }
    public CatalogMatchApplyResult? Apply { get; set; }
    public CatalogImportUnlinkedResult? Import { get; set; }
    public string? Error { get; set; }
}

public sealed class CatalogConnectJobsStateDto
{
    public CatalogConnectJobDto? Active { get; set; }
    public CatalogConnectJobDto? LatestPreview { get; set; }
    public CatalogConnectJobDto? LatestCompleted { get; set; }
}

public sealed class CatalogConnectApplyPayload
{
    public List<CatalogMatchApplyItem> Items { get; set; } = [];
    public bool ImportUnmatched { get; set; }
}

public sealed class CatalogConnectJobService
{
    public const string ImportOperationType = "catalog-connect-import";
    public const string MatchOperationType = "catalog-connect-match";
    public const string ApplyOperationType = "catalog-match";
    public const string BatchType = "catalog-connect";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly DataContext _db;
    private readonly CatalogMatchService _catalogMatch;
    private readonly IOperationLogger _operationLogger;
    private readonly ILogger<CatalogConnectJobService> _logger;

    public CatalogConnectJobService(
        DataContext db,
        CatalogMatchService catalogMatch,
        IOperationLogger operationLogger,
        ILogger<CatalogConnectJobService> logger)
    {
        _db = db;
        _catalogMatch = catalogMatch;
        _operationLogger = operationLogger;
        _logger = logger;
    }

    public async Task<CatalogConnectJobDto> EnqueueImportAsync(
        MarketplaceConnection connection,
        int userId,
        string? operationId,
        CancellationToken ct)
    {
        CatalogMatchService.EnsureConnectionActive(connection);
        return await EnqueueAsync(
            connection,
            userId,
            CatalogConnectJobKind.Import,
            ImportOperationType,
            $"Read catalog: {connection.name}",
            operationId,
            payloadJson: null,
            ct);
    }

    public async Task<CatalogConnectJobDto> EnqueueMatchAsync(
        MarketplaceConnection connection,
        int userId,
        string? operationId,
        CancellationToken ct)
    {
        CatalogMatchService.EnsureConnectionActive(connection);
        return await EnqueueAsync(
            connection,
            userId,
            CatalogConnectJobKind.Match,
            MatchOperationType,
            $"Match catalog: {connection.name}",
            operationId,
            payloadJson: null,
            ct);
    }

    public async Task<CatalogConnectJobDto> EnqueueApplyAsync(
        MarketplaceConnection connection,
        int userId,
        IReadOnlyList<CatalogMatchApplyItem> items,
        bool importUnmatched,
        string? operationId,
        CancellationToken ct)
    {
        CatalogMatchService.EnsureConnectionActive(connection);
        if (items.Count == 0)
            throw ApiException.BadRequest(code: "match_rows_required");

        var payload = JsonSerializer.Serialize(new CatalogConnectApplyPayload
        {
            Items = items.ToList(),
            ImportUnmatched = importUnmatched,
        }, JsonOptions);

        return await EnqueueAsync(
            connection,
            userId,
            CatalogConnectJobKind.Apply,
            ApplyOperationType,
            $"Link marketplace catalog",
            operationId,
            payload,
            ct);
    }

    public async Task<CatalogConnectJobsStateDto> GetStateAsync(int storeId, CancellationToken ct)
    {
        var active = await _db.pendingCatalogConnectJobs
            .AsNoTracking()
            .Where(j => j.storeId == storeId
                && (j.status == CatalogConnectJobStatus.Pending
                    || j.status == CatalogConnectJobStatus.InProgress))
            .OrderBy(j => j.createdAt)
            .FirstOrDefaultAsync(ct);

        var latestPreview = await _db.pendingCatalogConnectJobs
            .AsNoTracking()
            .Where(j => j.storeId == storeId
                && j.kind == CatalogConnectJobKind.Match
                && j.status == CatalogConnectJobStatus.Completed
                && j.resultJson != null)
            .OrderByDescending(j => j.completedAt ?? j.createdAt)
            .FirstOrDefaultAsync(ct);

        var latestCompleted = await _db.pendingCatalogConnectJobs
            .AsNoTracking()
            .Where(j => j.storeId == storeId
                && (j.status == CatalogConnectJobStatus.Completed
                    || j.status == CatalogConnectJobStatus.Failed
                    || j.status == CatalogConnectJobStatus.Cancelled))
            .OrderByDescending(j => j.completedAt ?? j.createdAt)
            .FirstOrDefaultAsync(ct);

        return new CatalogConnectJobsStateDto
        {
            Active = active == null ? null : ToDto(active),
            LatestPreview = latestPreview == null ? null : ToDto(latestPreview),
            LatestCompleted = latestCompleted == null ? null : ToDto(latestCompleted),
        };
    }

    public async Task ProcessJobAsync(PendingCatalogConnectJob job, CancellationToken ct)
    {
        job.status = CatalogConnectJobStatus.InProgress;
        job.lastAttemptAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (await OperationLifecycle.IsStopRequestedAsync(_db, job.operationId, ct))
        {
            await MarkTerminalAsync(job, CatalogConnectJobStatus.Cancelled, "cancelled", "Stopped", ct);
            return;
        }

        var connection = await _db.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == job.connectionId && c.storeId == job.storeId, ct);
        if (connection == null)
        {
            await MarkTerminalAsync(job, CatalogConnectJobStatus.Failed, "failed", "Connection not found", ct);
            return;
        }

        try
        {
            CatalogMatchService.EnsureConnectionActive(connection);
            switch (job.kind)
            {
                case CatalogConnectJobKind.Import:
                    await ProcessImportAsync(job, connection, ct);
                    break;
                case CatalogConnectJobKind.Match:
                    await ProcessMatchAsync(job, connection, ct);
                    break;
                case CatalogConnectJobKind.Apply:
                    await ProcessApplyAsync(job, connection, ct);
                    break;
                default:
                    await MarkTerminalAsync(job, CatalogConnectJobStatus.Failed, "failed", "Unknown job kind", ct);
                    break;
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Catalog connect job {OperationId} failed", job.operationId);
            await MarkTerminalAsync(job, CatalogConnectJobStatus.Failed, "failed", ex.Message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Catalog connect job {OperationId} failed", job.operationId);
            await MarkTerminalAsync(job, CatalogConnectJobStatus.Failed, "failed", ex.Message, ct);
        }
    }

    private async Task ProcessImportAsync(
        PendingCatalogConnectJob job,
        MarketplaceConnection connection,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.payloadJson))
        {
            job.payloadJson = Guid.NewGuid().ToString("N");
            await _db.SaveChangesAsync(ct);
        }

        var result = await _catalogMatch.QueueUnlinkedImportAsync(
            connection,
            job.authorUserId,
            ct,
            job.operationId,
            batchId: job.payloadJson,
            async (processed, total, queued) =>
            {
                job.processedItems = processed;
                job.totalItems = total ?? 0;
                job.queuedCount = queued;
                await _db.SaveChangesAsync(ct);
            });

        job.queuedCount = result.Queued;
        job.resultJson = JsonSerializer.Serialize(result, JsonOptions);
        var summary = result.Queued == 0
            ? "No unlinked products to import"
            : $"Queued {result.Queued} products";
        await MarkTerminalAsync(job, CatalogConnectJobStatus.Completed, "completed", summary, ct);
    }

    private async Task ProcessMatchAsync(
        PendingCatalogConnectJob job,
        MarketplaceConnection connection,
        CancellationToken ct)
    {
        var result = await _catalogMatch.PreviewAsync(
            connection,
            page: 1,
            pageSize: CatalogMatchService.MaxRemoteProducts,
            statusFilter: "all",
            ct,
            job.operationId,
            async (processed, total, mode) =>
            {
                job.mode = mode;
                job.processedItems = processed;
                job.totalItems = total ?? 0;
                await _db.SaveChangesAsync(ct);
            });

        job.mode = result.Mode;
        job.processedItems = result.Total;
        job.totalItems = Math.Max(job.totalItems, result.Total);
        job.resultJson = JsonSerializer.Serialize(result, JsonOptions);
        await MarkTerminalAsync(
            job,
            CatalogConnectJobStatus.Completed,
            "completed",
            $"Matched {result.MatchedCount}, unmatched {result.UnmatchedCount}",
            ct);
    }

    private async Task ProcessApplyAsync(
        PendingCatalogConnectJob job,
        MarketplaceConnection connection,
        CancellationToken ct)
    {
        var payload = string.IsNullOrWhiteSpace(job.payloadJson)
            ? null
            : JsonSerializer.Deserialize<CatalogConnectApplyPayload>(job.payloadJson, JsonOptions);
        if (payload?.Items == null || payload.Items.Count == 0)
        {
            await MarkTerminalAsync(job, CatalogConnectJobStatus.Failed, "failed", "No match rows to apply", ct);
            return;
        }

        job.totalItems = payload.Items.Count;
        await _db.SaveChangesAsync(ct);

        var result = await _catalogMatch.ApplyAsync(
            connection,
            payload.Items,
            payload.ImportUnmatched,
            job.authorUserId,
            ct,
            job.operationId);

        job.queuedCount = result.Imported;
        job.processedItems = result.Linked + result.Imported + result.Skipped + result.Failed;
        job.resultJson = JsonSerializer.Serialize(result, JsonOptions);

        if (await OperationLifecycle.IsStopRequestedAsync(_db, job.operationId, ct))
        {
            await MarkTerminalAsync(job, CatalogConnectJobStatus.Cancelled, "cancelled", "Stopped", ct, finishOperation: false);
            return;
        }

        var status = result.Failed == 0
            ? "completed"
            : (result.Linked > 0 || result.Imported > 0 ? "partial" : "failed");
        var jobStatus = status == "failed"
            ? CatalogConnectJobStatus.Failed
            : CatalogConnectJobStatus.Completed;
        await MarkTerminalAsync(
            job,
            jobStatus,
            status,
            $"Linked {result.Linked}, imported {result.Imported}, failed {result.Failed}",
            ct,
            finishOperation: false);
    }

    private async Task<CatalogConnectJobDto> EnqueueAsync(
        MarketplaceConnection connection,
        int userId,
        CatalogConnectJobKind kind,
        string operationType,
        string title,
        string? operationId,
        string? payloadJson,
        CancellationToken ct)
    {
        var existing = await _db.pendingCatalogConnectJobs
            .Where(j => j.storeId == connection.storeId
                && j.connectionId == connection.Id
                && j.kind == kind
                && (j.status == CatalogConnectJobStatus.Pending
                    || j.status == CatalogConnectJobStatus.InProgress))
            .OrderBy(j => j.createdAt)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
            return ToDto(existing);

        var clientStarted = !string.IsNullOrWhiteSpace(operationId);
        operationId = clientStarted
            ? operationId!.Trim()
            : Guid.NewGuid().ToString("N");

        if (!clientStarted)
        {
            await _operationLogger.StartAsync(
                operationId,
                connection.storeId,
                userId,
                operationType,
                title,
                connectionId: connection.Id,
                connectionName: connection.name,
                batchId: operationId,
                batchType: BatchType);
        }

        var job = new PendingCatalogConnectJob
        {
            storeId = connection.storeId,
            connectionId = connection.Id,
            authorUserId = userId,
            operationId = operationId,
            kind = kind,
            status = CatalogConnectJobStatus.Pending,
            payloadJson = payloadJson,
            createdAt = DateTime.UtcNow,
        };
        _db.pendingCatalogConnectJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    private async Task MarkTerminalAsync(
        PendingCatalogConnectJob job,
        CatalogConnectJobStatus jobStatus,
        string operationStatus,
        string summary,
        CancellationToken ct,
        bool finishOperation = true)
    {
        job.status = jobStatus;
        job.lastError = jobStatus == CatalogConnectJobStatus.Failed ? summary : job.lastError;
        job.completedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (finishOperation)
            await _operationLogger.FinishAsync(job.operationId, job.storeId, operationStatus, summary);
    }

    private static CatalogConnectJobDto ToDto(PendingCatalogConnectJob job)
    {
        var dto = new CatalogConnectJobDto
        {
            OperationId = job.operationId,
            ConnectionId = job.connectionId,
            Kind = KindName(job.kind),
            Status = StatusName(job.status),
            Mode = job.mode,
            TotalItems = job.totalItems,
            ProcessedItems = job.processedItems,
            QueuedCount = job.queuedCount,
            Error = job.lastError,
        };

        if (string.IsNullOrWhiteSpace(job.resultJson))
            return dto;

        try
        {
            switch (job.kind)
            {
                case CatalogConnectJobKind.Match:
                    dto.Preview = JsonSerializer.Deserialize<CatalogMatchPreviewResult>(
                        job.resultJson, JsonOptions);
                    break;
                case CatalogConnectJobKind.Apply:
                    dto.Apply = JsonSerializer.Deserialize<CatalogMatchApplyResult>(
                        job.resultJson, JsonOptions);
                    break;
                case CatalogConnectJobKind.Import:
                    dto.Import = JsonSerializer.Deserialize<CatalogImportUnlinkedResult>(
                        job.resultJson, JsonOptions);
                    dto.ImportBatchId = dto.Import?.BatchId;
                    break;
            }
        }
        catch (JsonException)
        {
            /* keep progress fields even if result JSON is unreadable */
        }

        return dto;
    }

    private static string KindName(CatalogConnectJobKind kind) => kind switch
    {
        CatalogConnectJobKind.Match => "match",
        CatalogConnectJobKind.Apply => "apply",
        _ => "import",
    };

    private static string StatusName(CatalogConnectJobStatus status) => status switch
    {
        CatalogConnectJobStatus.InProgress => "running",
        CatalogConnectJobStatus.Completed => "completed",
        CatalogConnectJobStatus.Failed => "failed",
        CatalogConnectJobStatus.Cancelled => "cancelled",
        _ => "pending",
    };
}
