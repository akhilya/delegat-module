using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Delegat.Hubs;
using Delegat.Models;

namespace Delegat.Services;

public class OperationLogger : IOperationLogger
{
    private readonly IHubContext<OperationHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OperationLogger> _logger;

    public OperationLogger(
        IHubContext<OperationHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<OperationLogger> logger)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
                                  int? connectionId = null, string? connectionName = null,
                                  string? batchId = null, string? batchType = null, int? totalItems = null)
    {
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

            // Resolve author name for the SignalR event so the panel can show who started this
            authorUserName = await db.Users
                .Where(u => u.Id == authorUserId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync();
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
