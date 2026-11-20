using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat.Models;
using Delegat.Models.Integration;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Integration;

/// <summary>
/// Authoritative owner of durable integration commands. Responsible for
/// atomic admission with request-key deduplication, the initial operation
/// projection, user Stop, guarded history deletion and terminal aggregation.
/// Stage dispatch/confirmation is driven by <see cref="IntegrationExecutionWorker"/>.
/// </summary>
public class IntegrationCoordinator
{
    public const string OperationType = "integration";

    private readonly DataContext _db;
    private readonly IOperationLogger _operationLogger;
    private readonly ILogger<IntegrationCoordinator> _logger;

    public IntegrationCoordinator(
        DataContext db,
        IOperationLogger operationLogger,
        ILogger<IntegrationCoordinator> logger)
    {
        _db = db;
        _operationLogger = operationLogger;
        _logger = logger;
    }

    /// <summary>
    /// Admits a prepared command. The same (scope, requestKey, body) returns the
    /// same command; the same key with a different body conflicts before any
    /// effect. Command, stages and the operation projection commit atomically.
    /// </summary>
    public async Task<IntegrationAdmitResult> AdmitAsync(
        IntegrationAdmitRequest request, bool v1Enabled, CancellationToken ct)
    {
        if (!v1Enabled)
            throw new IntegrationExecutionDisabledException(
                "Integration execution V1 is not enabled on this instance");

        var scopeKey = ComputeScopeKey(request);
        var requestHash = ComputeHash(request.InputJson);
        var requestKey = string.IsNullOrWhiteSpace(request.RequestKey)
            ? null
            : request.RequestKey.Trim();

        // The dedupe read and the admission transaction must run inside the
        // execution strategy: retry-enabled providers reject work performed
        // inside an unmanaged transaction.
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(
            _ => AdmitInnerAsync(request, scopeKey, requestKey, requestHash, ct), ct);
    }

    private async Task<IntegrationAdmitResult> AdmitInnerAsync(
        IntegrationAdmitRequest request, string scopeKey, string? requestKey,
        string requestHash, CancellationToken ct)
    {
        if (requestKey != null)
        {
            var existing = await _db.integrationCommands
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.scopeKey == scopeKey && c.requestKey == requestKey, ct);
            if (existing != null)
            {
                if (existing.requestHash != requestHash)
                    throw new IntegrationRequestConflictException(
                        $"Request key '{requestKey}' was already used with a different body in this scope");
                return new IntegrationAdmitResult(existing.commandId, existing.operationId, IsDuplicate: true);
            }
        }

        var commandId = Guid.NewGuid().ToString("N");
        var operationId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        var command = new IntegrationCommand
        {
            commandId = commandId,
            operationId = operationId,
            storeId = request.StoreId,
            authorUserId = request.AuthorUserId,
            authorApiKeyId = request.AuthorApiKeyId,
            connectionId = request.ConnectionId,
            scopeKey = scopeKey,
            kind = request.Kind,
            requestKey = requestKey,
            requestHash = requestHash,
            inputJson = request.InputJson,
            title = request.Title,
            createdAt = now,
            updatedAt = now
        };

        var stageEntities = request.Stages.Select(s => new IntegrationStage
        {
            command = command,
            itemKey = s.ItemKey,
            stageKey = s.StageKey,
            kind = s.Kind,
            position = s.Position,
            payloadJson = s.PayloadJson,
            payloadFileKey = s.PayloadFileKey,
            dependsOnJson = JsonSerializer.Serialize(s.DependsOn ?? (IReadOnlyList<string>)[]),
            batchKey = s.BatchKey,
            isEffectful = s.IsEffectful,
            state = IntegrationStageState.Ready,
            createdAt = now,
            updatedAt = now
        }).ToList();

        var operation = new Operation
        {
            operationId = operationId,
            storeId = request.StoreId,
            authorUserId = request.AuthorUserId,
            authorApiKeyId = request.AuthorApiKeyId,
            type = OperationType,
            title = request.Title ?? request.Kind,
            status = OperationLifecycle.Running,
            connectionId = request.ConnectionId,
            batchId = commandId,
            batchType = OperationType,
            totalItems = request.Stages.Select(s => s.ItemKey).Distinct().Count(),
            completedItems = 0,
            failedItems = 0,
            createdAt = now
        };

        // Command + stages + projection are one atomic admission.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.integrationCommands.Add(command);
        _db.integrationStages.AddRange(stageEntities);
        _db.operations.Add(operation);
        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException) when (requestKey != null)
        {
            // Unique (scopeKey, requestKey) raced us: read the winner and compare bodies.
            var winner = await _db.integrationCommands
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.scopeKey == scopeKey && c.requestKey == requestKey, CancellationToken.None);
            if (winner == null)
                throw;
            if (winner.requestHash != requestHash)
                throw new IntegrationRequestConflictException(
                    $"Request key '{requestKey}' was already used with a different body in this scope");
            return new IntegrationAdmitResult(winner.commandId, winner.operationId, IsDuplicate: true);
        }

        return new IntegrationAdmitResult(commandId, operationId, IsDuplicate: false);
    }

    /// <summary>
    /// User Stop: prevents new stage writes; in-flight/accepted stages are
    /// still confirmed (no implicit remote rollback).
    /// </summary>
    public async Task<bool> RequestStopByOperationIdAsync(string operationId, CancellationToken ct)
    {
        var command = await _db.integrationCommands
            .FirstOrDefaultAsync(c => c.operationId == operationId, ct);
        if (command == null)
            return false;

        if (!IntegrationCommandState.IsTerminal(command.state) && !command.stopRequested)
        {
            command.stopRequested = true;
            command.updatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var pendingCount = await _db.integrationStages
                .CountAsync(s => s.commandId == command.Id
                    && (s.state == IntegrationStageState.Ready
                        || s.state == IntegrationStageState.Deferred), ct);
            await _operationLogger.LogAsync(operationId, command.storeId, "warning",
                pendingCount > 0
                    ? $"Stop requested; {pendingCount} pending stage(s) will not be sent"
                    : "Stop requested; already-sent stages will be confirmed but nothing new will be sent");
        }
        return true;
    }

    /// <summary>Operation IDs whose coordinator-owned commands are unresolved.</summary>
    public async Task<HashSet<string>> GetUnresolvedOperationIdsAsync(
        IEnumerable<string> operationIds, CancellationToken ct)
    {
        var ids = operationIds.ToList();
        var unresolved = await _db.integrationCommands
            .AsNoTracking()
            .Where(c => ids.Contains(c.operationId)
                && !IntegrationTerminalStates.Contains(c.state))
            .Select(c => c.operationId)
            .ToListAsync(ct);
        return unresolved.ToHashSet();
    }

    /// <summary>Whether this operation is backed by an unresolved command.</summary>
    public Task<bool> IsUnresolvedCommandOperationAsync(string operationId, CancellationToken ct) =>
        _db.integrationCommands
            .AsNoTracking()
            .AnyAsync(c => c.operationId == operationId
                && !IntegrationTerminalStates.Contains(c.state), ct);

    /// <summary>Whether this operation belongs to the durable executor at all.</summary>
    public Task<bool> IsCommandOperationAsync(string operationId, CancellationToken ct) =>
        _db.integrationCommands.AsNoTracking().AnyAsync(c => c.operationId == operationId, ct);

    private static readonly string[] IntegrationTerminalStates =
    [
        IntegrationCommandState.Completed,
        IntegrationCommandState.Partial,
        IntegrationCommandState.Failed,
        IntegrationCommandState.Cancelled,
        IntegrationCommandState.Unknown
    ];

    internal static string ComputeScopeKey(IntegrationAdmitRequest request) =>
        $"{request.StoreId}:{request.ConnectionId?.ToString() ?? "-"}:{request.Kind}";

    internal static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
