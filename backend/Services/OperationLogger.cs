using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Delegat.Hubs;
using Delegat.Models;
using Delegat.Services.Ai.Agent;
using Delegat.Infrastructure.Security;

namespace Delegat.Services;

public class OperationLogger : IOperationLogger
{
    private readonly IHubContext<OperationHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OperationLogger> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public OperationLogger(
        IHubContext<OperationHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<OperationLogger> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
                                  int? connectionId = null, string? connectionName = null,
                                  string? batchId = null, string? batchType = null, int? totalItems = null,
                                  int? authorApiKeyId = null)
    {
        if (authorApiKeyId == null && _httpContextAccessor?.HttpContext?.Items[CabinetRequestInvoker.OriginApiKeyIdItemKey] is int originApiKeyId)
            authorApiKeyId = originApiKeyId;
        string? authorUserName = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            db.operations.Add(new Operation
            {
                operationId = operationId,
                storeId = storeId,
                authorUserId = authorUserId,
                authorApiKeyId = authorApiKeyId,
                type = type,
                title = title,
                status = "running",
                connectionId = connectionId,
                connectionName = connectionName,
                batchId = batchId,
                batchType = batchType,
                totalItems = totalItems,
                createdAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            authorUserName = await ResolveAuthorNameAsync(db, authorUserId, authorApiKeyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist operation start {OperationId}", operationId);
        }

        await _hub.Clients.Group($"store-{storeId}")
            .SendAsync("OperationStarted", new
            {
                operationId,
                storeId,
                authorUserId,
                authorUserName,
                type,
                title,
                status = "running",
                connectionId,
                connectionName,
                batchId,
                batchType,
                totalItems,
                createdAt = DateTime.UtcNow
            });
    }

    public async Task LogAsync(string operationId, int storeId, string level, string message)
    {
        message = MediaExportGrantSecrets.Redact(message);
        var timestamp = DateTime.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            db.operationLogEntries.Add(new OperationLogEntry
            {
                operationId = operationId,
                level = level,
                message = message,
                timestamp = timestamp
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist operation log {OperationId}", operationId);
        }

        await _hub.Clients.Group($"store-{storeId}")
            .SendAsync("OperationLog", new
            {
                operationId,
                level,
                message,
                timestamp
            });
    }

    public async Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed)
    {
        var timestamp = DateTime.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var op = await db.operations.FirstOrDefaultAsync(o => o.operationId == operationId);
            if (op != null)
            {
                if (OperationLifecycle.IsTerminal(op.status))
                    return;
                op.totalItems = total;
                op.completedItems = completed;
                op.failedItems = failed;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist operation progress {OperationId}", operationId);
        }

        await _hub.Clients.Group($"store-{storeId}")
            .SendAsync("OperationProgress", new
            {
                operationId,
                total,
                completed,
                failed,
                pending = total - completed - failed,
                timestamp
            });
    }

    public async Task FinishAsync(string operationId, int storeId, string status, string? summary = null)
    {
        var timestamp = DateTime.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var op = await db.operations.FirstOrDefaultAsync(o => o.operationId == operationId);
            if (op != null)
            {
                if (!OperationLifecycle.ShouldApplyFinish(op.status, status))
                    return;
                op.status = status;
                op.summary = summary;
                op.finishedAt = timestamp;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist operation finish {OperationId}", operationId);
        }

        await _hub.Clients.Group($"store-{storeId}")
            .SendAsync("OperationFinished", new
            {
                operationId,
                status,
                summary,
                timestamp
            });
    }

    /// <inheritdoc />
    public async Task NotifyFinishedAsync(string operationId, int storeId, string status, string? summary = null)
    {
        await _hub.Clients.Group($"store-{storeId}")
            .SendAsync("OperationFinished", new
            {
                operationId,
                status,
                summary,
                timestamp = DateTime.UtcNow
            });
    }

    private static async Task<string?> ResolveAuthorNameAsync(
        DataContext db, int authorUserId, int? authorApiKeyId)
    {
        if (authorApiKeyId is int keyId)
        {
            var key = await db.userApiKeys
                .AsNoTracking()
                .Where(k => k.Id == keyId)
                .Select(k => new { k.name, k.keyPrefix, k.last4 })
                .FirstOrDefaultAsync();
            return key == null
                ? "API"
                : OperationAuthor.ApiKey(key.name, key.keyPrefix, key.last4);
        }

        return await db.Users
            .Where(u => u.Id == authorUserId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync();
    }

    public async Task UpdateStatusAsync(string operationId, int storeId, string status)
    {
        await _hub.Clients.Group($"store-{storeId}")
            .SendAsync("OperationStatusChanged", new
            {
                operationId,
                status
            });
    }
}
