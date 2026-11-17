using System.Security.Cryptography;
using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// One sandbox output after archive ingestion: trusted spec plus the byte-verified
/// length/hash and a private, seekable spool. Never deserialized from tool JSON.
/// </summary>
public sealed record VerifiedGeneratedOutput(
    GeneratedOutputSpec Spec,
    long Length,
    string Sha256,
    Stream Stream);

/// <summary>
/// Immutable generated-artifact stage and the separately confirmed exact import.
/// Staged bytes live privately under <c>__ai-generated/</c>; an import binds one
/// durable receipt per exact artifact+destination and never replays an ambiguous
/// write. This is a per-import fence, not durable AI-run recovery.
/// </summary>
public sealed class GeneratedArtifactService
{
    public const string StoragePrefix = "__ai-generated/";

    private const string StateCapturing = "capturing";
    private const string StateReady = "ready";
    private const string ReceiptStarted = "started";
    private const string ReceiptCompleted = "completed";

    private static readonly JsonSerializerOptions BindingJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The default provider strategy retries on failure (Program.cs enables it for
    /// MySQL), which would replay an ambiguous <c>SaveChanges</c> and forbid manual
    /// transactions outside <c>CreateExecutionStrategy().ExecuteAsync</c>. This
    /// wrapper sets the async-local <see cref="ExecutionStrategy.Current"/> for one
    /// attempt: nested provider commands run without retries, user transactions are
    /// allowed, and nothing is ever replayed - ambiguity always surfaces to us.
    /// </summary>
    private sealed class SingleAttemptExecutionStrategy(DbContext context)
        : ExecutionStrategy(context, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private readonly DataContext _db;
    private readonly IStorageService _storage;
    private readonly QuotaService _quota;
    private readonly AgentSessionResolver _sessions;
    private readonly TimeProvider _time;
    private readonly ILogger<GeneratedArtifactService> _logger;

    public GeneratedArtifactService(
        DataContext db,
        IStorageService storage,
        QuotaService quota,
        AgentSessionResolver sessions,
        ILogger<GeneratedArtifactService> logger,
        TimeProvider? time = null)
    {
        _db = db;
        _storage = storage;
        _quota = quota;
        _sessions = sessions;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Reserves artifact rows, uploads the verified streams, then flips the batch to
    /// ready atomically. Rows are reserved inside one store-locked transaction
    /// before any object I/O, so a failed reservation can never leave untracked
    /// uploaded bytes and concurrent captures cannot both pass the quota check.
    /// </summary>
    public Task<IReadOnlyList<GeneratedArtifactMetadata>> StageCapturedAsync(
        int storeId,
        int userId,
        int sandboxRunId,
        IReadOnlyList<VerifiedGeneratedOutput> outputs,
        CancellationToken ct)
    {
        EnsureNoAmbientTransaction();
        return new SingleAttemptExecutionStrategy(_db).ExecuteAsync(
            () => StageCapturedCoreAsync(storeId, userId, sandboxRunId, outputs, ct));
    }

    private async Task<IReadOnlyList<GeneratedArtifactMetadata>> StageCapturedCoreAsync(
        int storeId,
        int userId,
        int sandboxRunId,
        IReadOnlyList<VerifiedGeneratedOutput> outputs,
        CancellationToken ct)
    {
        if (outputs.Count == 0 || outputs.Count > GeneratedFileContract.MaxOutputs)
            throw new GeneratedArtifactException("ai_artifact_invalid_request");

        var session = await ResolveActorAsync(storeId, userId, ct);
        RequireFilesManage(session);

        var now = _time.GetUtcNow().UtcDateTime;
        var expires = GeneratedFileContract.TruncateToMicroseconds(
            now.Add(GeneratedFileContract.ArtifactLifetime));
        var rows = outputs.Select(output => new AiGeneratedArtifact
        {
            Id = GeneratedFileContract.NewArtifactId(),
            storeId = storeId,
            userId = userId,
            sandboxRunId = sandboxRunId,
            fileName = output.Spec.FileName,
            contentType = output.Spec.ContentType,
            length = output.Length,
            sha256 = output.Sha256,
            createdAtUtc = now,
            expiresAtUtc = expires,
            state = StateCapturing,
        }).ToList();
        foreach (var row in rows)
            row.storageKey = $"{StoragePrefix}{storeId}/{userId}/{row.Id}/{row.fileName}";

        // Quota check, row reservation and commit stay inside one short
        // store-locked transaction; no I/O can interleave between them.
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await LockStoreRowAsync(storeId, ct);
            await _quota.EnsureStorageQuotaAsync(storeId, rows.Sum(r => r.length));
            _db.aiGeneratedArtifacts.AddRange(rows);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (ApiException)
        {
            DetachOwned(rows);
            throw;
        }
        catch (GeneratedArtifactException)
        {
            DetachOwned(rows);
            throw;
        }
        catch (OperationCanceledException)
        {
            // Cancel is honest: pending rows are detached so a later audit save
            // cannot publish them, then the cancellation propagates.
            DetachOwned(rows);
            throw;
        }
        catch (Exception ex)
        {
            // The reservation is ambiguous; no bytes were uploaded yet. Detach so
            // a later audit SaveChanges cannot accidentally persist pending rows.
            _logger.LogWarning(ex, "Generated artifact reservation ambiguous");
            DetachOwned(rows);
            throw new GeneratedArtifactException("ai_artifact_capture_unknown", outcomeUnknown: true);
        }

        // Track keys BEFORE calling UploadAsync: an upload that wrote bytes and
        // then returned false or threw still produces an object that must be
        // cleaned or accounted for.
        var attemptedKeys = new List<string>();
        try
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var stream = outputs[i].Stream;
                if (stream.CanSeek)
                    stream.Position = 0;
                attemptedKeys.Add(rows[i].storageKey);
                var ok = await _storage.UploadAsync(
                    stream, rows[i].storageKey, rows[i].length, rows[i].contentType, ct);
                if (!ok)
                    throw new GeneratedArtifactException("ai_artifact_capture_failed");
            }
        }
        catch (Exception ex) when (ex is not GeneratedArtifactException and not OperationCanceledException)
        {
            await CleanupCaptureAsync(rows, attemptedKeys);
            throw new GeneratedArtifactException("ai_artifact_capture_failed");
        }
        catch
        {
            await CleanupCaptureAsync(rows, attemptedKeys);
            throw;
        }

        foreach (var row in rows)
            row.state = StateReady;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Ambiguous ready flip: leave reserved rows and uploaded keys for TTL
            // cleanup; the model never receives handles it cannot trust.
            DetachOwned(rows);
            throw new GeneratedArtifactException("ai_artifact_capture_unknown", outcomeUnknown: true);
        }

        return rows.Select(row => new GeneratedArtifactMetadata(
            row.Id, row.fileName, row.contentType, row.length, row.sha256,
            row.expiresAtUtc, GeneratedFileContract.TemporaryDownloadUrl(row.Id)))
            .ToList();
    }

    /// <summary>
    /// Temporary download of a staged artifact by its owning user in their
    /// selected store. Read-only end to end: no rows, receipts, uploads or TTL
    /// changes. Bytes are spooled and re-hashed, then ownership, membership,
    /// ai.use, expiry and every immutable metadata field are checked AGAIN
    /// before the stream is handed out - a revocation or cleanup landing
    /// mid-read can never leak bytes. No byte is sent until the second check
    /// succeeds; an HTTP transfer already admitted is not retroactively
    /// revocable.
    /// </summary>
    public async Task<GeneratedArtifactDownload> OpenDownloadAsync(
        string artifactId, int storeId, int userId, CancellationToken ct)
    {
        FileStream? spool = null;
        try
        {
            if (!GeneratedFileContract.IsValidArtifactId(artifactId))
                throw new GeneratedArtifactException("ai_artifact_not_found");
            await RequireDownloadActorAsync(storeId, userId, ct);
            var row = await LoadDownloadAsync(artifactId, storeId, userId, ct);
            var expectedKey = $"{StoragePrefix}{storeId}/{userId}/{row.Id}/{row.fileName}";
            if (row.length < 0 || row.length > GeneratedFileContract.MaxFileBytes
                || !GeneratedFileContract.IsValidSha256(row.sha256)
                || !string.Equals(row.storageKey, expectedKey, StringComparison.Ordinal))
                throw new GeneratedArtifactException("ai_artifact_mismatch");
            var path = Path.Combine(Path.GetTempPath(), $"ai-download-{Guid.NewGuid():N}.tmp");
            spool = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            SetPrivateFileMode(path);
            await using (var input = await _storage.GetStreamAsync(row.storageKey, ct)
                ?? throw new GeneratedArtifactException("ai_artifact_not_found"))
            {
                var (bytes, sha) = await CopyHashingAsync(input, spool,
                    GeneratedFileContract.MaxFileBytes + 1, ct);
                if (bytes != row.length
                    || !string.Equals(sha, row.sha256, StringComparison.Ordinal))
                    throw new GeneratedArtifactException("ai_artifact_mismatch");
            }
            // Fresh database authorization and immutable metadata after object I/O.
            await RequireDownloadActorAsync(storeId, userId, ct);
            var fresh = await LoadDownloadAsync(artifactId, storeId, userId, ct);
            if (fresh.storageKey != row.storageKey || fresh.length != row.length
                || fresh.sha256 != row.sha256 || fresh.fileName != row.fileName
                || fresh.contentType != row.contentType
                || fresh.expiresAtUtc != row.expiresAtUtc)
                throw new GeneratedArtifactException("ai_artifact_mismatch");
            spool.Position = 0;
            var result = new GeneratedArtifactDownload(
                spool, row.fileName, row.contentType, row.length);
            spool = null; // ownership transfers to caller; every failure closes/deletes it
            return result;
        }
        catch (GeneratedArtifactException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generated artifact {ArtifactId} download failed", artifactId);
            throw new GeneratedArtifactException("ai_artifact_download_failed");
        }
        finally { if (spool != null) await spool.DisposeAsync(); }
    }

    private async Task RequireDownloadActorAsync(int storeId, int userId, CancellationToken ct)
    {
        var session = await ResolveActorAsync(storeId, userId, ct);
        if (!session.Permissions.Contains(Permissions.AiUse, StringComparer.OrdinalIgnoreCase))
            throw new GeneratedArtifactException("forbidden");
    }

    private async Task<AiGeneratedArtifact> LoadDownloadAsync(
        string artifactId, int storeId, int userId, CancellationToken ct)
    {
        var row = await _db.aiGeneratedArtifacts.AsNoTracking().SingleOrDefaultAsync(
            a => a.Id == artifactId && a.storeId == storeId && a.userId == userId, ct);
        if (row == null || row.state != StateReady)
            throw new GeneratedArtifactException("ai_artifact_not_found");
        if (row.expiresAtUtc <= _time.GetUtcNow().UtcDateTime)
            throw new GeneratedArtifactException("ai_artifact_expired");
        return row;
    }

    /// <summary>
    /// Capture preflight shared by proposal validation and the sandbox client:
    /// the actor must be a live member with explicit ai.use plus files.manage on a
    /// non-read-only account, and the store must exist. Runs before any POST or
    /// credential preparation so a forbidden capture never executes the script.
    /// </summary>
    internal async Task ValidateCaptureAsync(int storeId, int userId, CancellationToken ct)
    {
        var session = await ResolveActorAsync(storeId, userId, ct);
        if (!AgentAccess.CanRunPython(session.Permissions)
            || !AgentAccess.CanMutate(session.Permissions, Permissions.FilesManage))
            throw new GeneratedArtifactException("forbidden");
    }

    /// <summary>
    /// Proposal-time validation: the artifact must be a known ready handle of this
    /// actor/store whose exact metadata the model echoed back, with a live target.
    /// An already-completed receipt makes the import a safe idempotent operation
    /// that needs no extra quota; a started one is an honest unknown.
    /// </summary>
    public async Task<GeneratedFileImportApproval> ValidateImportAsync(
        GeneratedFileImportRequest request,
        int storeId,
        int userId,
        int step,
        CancellationToken ct)
    {
        var session = await ResolveActorAsync(storeId, userId, ct);
        RequireFilesManage(session);
        var artifact = await LoadArtifactAsync(request, storeId, userId, ct);
        ValidateArtifactMatch(artifact, request);
        await ValidateDestinationAsync(request, storeId, ct);

        var bindingId = ComputeBindingId(request, artifact, storeId, userId);
        var existing = await _db.aiGeneratedArtifactImports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == bindingId, ct);
        if (existing is { state: ReceiptCompleted, fileId: { } completedFileId })
        {
            // Re-presenting the exact completed import is idempotent: it writes
            // nothing, so it must not be refused on a full quota.
            if (!await CompletedTargetExistsAsync(existing, completedFileId, storeId, ct))
                throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
        }
        else if (existing != null)
        {
            // A started receipt means in-progress or unknown: zero I/O, no replay.
            throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
        }
        else
        {
            await _quota.EnsureFileQuotaAsync(storeId, 1);
            await _quota.EnsureStorageQuotaAsync(storeId, request.Length);
        }

        return new GeneratedFileImportApproval(
            step, artifact.Id, artifact.fileName, artifact.length, artifact.sha256,
            artifact.expiresAtUtc, request.Destination, storeId,
            request.ProductId, request.FolderId);
    }

    /// <summary>
    /// Execution-time import behind a confirmed plan. Claims the exact binding
    /// receipt before any byte I/O; a completed receipt resolves the same file, a
    /// started one is an honest unknown and is never replayed.
    /// </summary>
    public Task<GeneratedFileImportResult> ImportAsync(
        GeneratedFileImportRequest request,
        int storeId,
        int userId,
        CancellationToken ct)
    {
        EnsureNoAmbientTransaction();
        return new SingleAttemptExecutionStrategy(_db).ExecuteAsync(
            () => ImportCoreAsync(request, storeId, userId, ct));
    }

    private async Task<GeneratedFileImportResult> ImportCoreAsync(
        GeneratedFileImportRequest request,
        int storeId,
        int userId,
        CancellationToken ct)
    {
        var session = await ResolveActorAsync(storeId, userId, ct);
        RequireFilesManage(session);
        var artifact = await LoadArtifactAsync(request, storeId, userId, ct);
        ValidateArtifactMatch(artifact, request);
        await ValidateDestinationAsync(request, storeId, ct);

        var bindingId = ComputeBindingId(request, artifact, storeId, userId);
        var destinationKey = request.Destination == GeneratedFileContract.DestinationStore
            ? $"{storeId}/store/{Guid.NewGuid():N}/{artifact.fileName}"
            : $"{storeId}/{request.ProductId}/{Guid.NewGuid():N}/{artifact.fileName}";

        var claimed = false;
        AiGeneratedArtifactImport? existing;
        AiGeneratedArtifactImport? newReceipt = null;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await LockStoreRowAsync(storeId, ct);
            existing = await _db.aiGeneratedArtifactImports.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == bindingId, ct);
            if (existing == null)
            {
                // The new receipt's bytes are not yet counted; claim checks
                // additional length/1 before reserving.
                await _quota.EnsureFileQuotaAsync(storeId, 1);
                await _quota.EnsureStorageQuotaAsync(storeId, request.Length);
                newReceipt = new AiGeneratedArtifactImport
                {
                    Id = bindingId,
                    artifactId = artifact.Id,
                    storeId = storeId,
                    userId = userId,
                    destination = request.Destination,
                    productId = request.ProductId,
                    folderId = request.FolderId,
                    length = request.Length,
                    destinationKey = destinationKey,
                    createdAtUtc = _time.GetUtcNow().UtcDateTime,
                    state = ReceiptStarted,
                };
                _db.aiGeneratedArtifactImports.Add(newReceipt);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    claimed = true;
                }
                catch (DbUpdateException)
                {
                    // Lost the claim race: read the fence another context wrote.
                    _db.Entry(newReceipt).State = EntityState.Detached;
                    newReceipt = null;
                    existing = await _db.aiGeneratedArtifactImports.AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == bindingId, ct);
                }
            }
            await tx.CommitAsync(ct);
        }
        catch (ApiException)
        {
            DetachOwned([newReceipt]);
            throw;
        }
        catch (GeneratedArtifactException)
        {
            DetachOwned([newReceipt]);
            throw;
        }
        catch (OperationCanceledException)
        {
            DetachOwned([newReceipt]);
            throw;
        }
        catch (Exception ex)
        {
            // Claim or commit failed ambiguously: detach the pending receipt so
            // the host audit save cannot publish it, and report unknown rather
            // than a raw database message.
            _logger.LogWarning(ex, "Generated artifact {ArtifactId} import claim failed", artifact.Id);
            DetachOwned([newReceipt]);
            throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
        }

        if (!claimed)
        {
            if (existing == null)
                throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
            if (existing.state == ReceiptCompleted && existing.fileId is { } completedFileId)
                return await ResolveCompletedAsync(existing, artifact, completedFileId, storeId, ct);
            // A started receipt means in-progress or unknown: zero I/O, no replay.
            throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
        }

        // Spool the staged object once to a private temp and verify bytes before
        // writing the destination. No whole-file buffering in memory.
        var tempPath = Path.Combine(Path.GetTempPath(), $"ai-import-{Guid.NewGuid():N}.tmp");
        var uploadAttempted = false;
        try
        {
            await using (var temp = new FileStream(
                             tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose))
            {
                SetPrivateFileMode(tempPath);
                await using (var input = await _storage.GetStreamAsync(artifact.storageKey, ct)
                             ?? throw new GeneratedArtifactException("ai_artifact_not_found"))
                {
                    var (bytes, sha) = await CopyHashingAsync(
                        input, temp, GeneratedFileContract.MaxFileBytes + 1, ct);
                    if (bytes != artifact.length
                        || !string.Equals(sha, artifact.sha256, StringComparison.Ordinal))
                        throw new GeneratedArtifactException("ai_artifact_mismatch");
                }
                temp.Position = 0;
                // Marked before the await: an upload that wrote bytes then
                // returned false or threw still leaves an owned object to delete.
                uploadAttempted = true;
                var uploaded = await _storage.UploadAsync(
                    temp, destinationKey, artifact.length, artifact.contentType, ct);
                if (!uploaded)
                    throw new GeneratedArtifactException("ai_artifact_storage_failed");
            }
        }
        catch (GeneratedArtifactException)
        {
            if (!await ReleaseClaimAsync(bindingId, destinationKey, uploadAttempted))
                throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
            throw;
        }
        catch (OperationCanceledException)
        {
            // A cancelled import is an honest unknown: keep the fence and any
            // uploaded bytes; the durable receipt records where they went.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generated artifact {ArtifactId} import spool/upload failed", artifact.Id);
            if (!await ReleaseClaimAsync(bindingId, destinationKey, uploadAttempted))
                throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
            throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
        }

        // Short final transaction: re-validate, write the typed file row and mark
        // the receipt completed atomically. No external I/O inside.
        var saveAttempted = false;
        object? pendingFile = null;
        AiGeneratedArtifactImport? pendingReceipt = null;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await LockStoreRowAsync(storeId, ct);
            var recheck = await ResolveActorAsync(storeId, userId, ct);
            RequireFilesManage(recheck);
            await ValidateDestinationAsync(request, storeId, ct);
            // The started receipt already reserves bytes/count; additional is 0.
            await _quota.EnsureFileQuotaAsync(storeId, 0);
            await _quota.EnsureStorageQuotaAsync(storeId, 0);
            var fresh = await _db.aiGeneratedArtifacts
                .FirstOrDefaultAsync(a => a.Id == artifact.Id, ct);
            if (fresh == null || fresh.state != StateReady
                || fresh.expiresAtUtc <= _time.GetUtcNow().UtcDateTime)
                throw new GeneratedArtifactException("ai_artifact_expired");
            var receipt = await _db.aiGeneratedArtifactImports
                .FirstOrDefaultAsync(r => r.Id == bindingId, ct)
                ?? throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
            pendingReceipt = receipt;
            if (receipt.state != ReceiptStarted)
                throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);

            int fileId;
            if (request.Destination == GeneratedFileContract.DestinationStore)
            {
                var entity = new StoreFile
                {
                    folderId = request.FolderId,
                    displayName = artifact.fileName,
                    fileType = Enums.FileType.Other,
                };
                entity.Fill(destinationKey, userId, storeId, artifact.contentType, artifact.length);
                _db.storeFiles.Add(entity);
                pendingFile = entity;
                saveAttempted = true;
                await _db.SaveChangesAsync(ct);
                fileId = entity.Id;
            }
            else
            {
                var entity = new Models.File
                {
                    folderId = request.FolderId,
                    displayName = artifact.fileName,
                    fileType = Enums.FileType.Other,
                };
                entity.AddFill(new Models.File.AddForm(
                    destinationKey, userId, request.ProductId!.Value,
                    artifact.contentType, artifact.length));
                _db.files.Add(entity);
                pendingFile = entity;
                saveAttempted = true;
                await _db.SaveChangesAsync(ct);
                fileId = entity.Id;
            }
            receipt.state = ReceiptCompleted;
            receipt.fileId = fileId;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var downloadUrl = request.Destination == GeneratedFileContract.DestinationStore
                ? $"/api/store-storage/files/{fileId}/download"
                : $"/api/files/{fileId}/download";
            return new GeneratedFileImportResult(
                request.Destination, fileId, request.ProductId, request.FolderId,
                artifact.fileName, artifact.contentType, artifact.length, artifact.sha256,
                downloadUrl, Idempotent: false, WrittenCount: 1);
        }
        catch (GeneratedArtifactException) when (!saveAttempted)
        {
            // Known pre-commit failure: remove only this newly claimed receipt once
            // the owned destination object is gone, so a new confirmed plan can retry.
            if (!await ReleaseClaimAsync(bindingId, destinationKey, uploadAttempted))
                throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
            throw;
        }
        catch (Exception ex) when (!saveAttempted && ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Generated artifact {ArtifactId} import validation failed", artifact.Id);
            if (!await ReleaseClaimAsync(bindingId, destinationKey, uploadAttempted))
                throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
            throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
        }
        catch (Exception ex)
        {
            // Every failure or cancellation once a save was attempted is
            // ambiguous: never delete the destination, never retry the save,
            // and detach ONLY the owned pending file/receipt entries (whatever
            // state they ended in) so the host's audit SaveChanges cannot
            // replay them.
            if (ex is not OperationCanceledException)
                _logger.LogWarning(
                    ex, "Generated artifact {ArtifactId} import commit ambiguous", artifact.Id);
            DetachOwned([pendingFile, pendingReceipt]);
            if (ex is OperationCanceledException && !saveAttempted)
                throw;
            throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);
        }
    }

    /// <summary>
    /// Expires staged artifacts. The object is deleted before the row so a failed
    /// delete keeps the row - and its bytes in the quota sum - for a later pass.
    /// Keys outside the generated namespace are never touched: malformed metadata
    /// retains the row rather than deleting an arbitrary path. Import receipts are
    /// never purged; a started one records an unknown write that needs operator
    /// reconciliation, not an automatic retry.
    /// </summary>
    public Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        EnsureNoAmbientTransaction();
        return new SingleAttemptExecutionStrategy(_db).ExecuteAsync(
            () => PurgeExpiredCoreAsync(ct));
    }

    private async Task<int> PurgeExpiredCoreAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var expired = await _db.aiGeneratedArtifacts
            .Where(a => a.expiresAtUtc <= now)
            .ToListAsync(ct);
        if (expired.Count == 0)
            return 0;

        var removed = new List<AiGeneratedArtifact>();
        foreach (var row in expired)
        {
            var expectedKey = $"{StoragePrefix}{row.storeId}/{row.userId}/{row.Id}/{row.fileName}";
            if (!string.Equals(row.storageKey, expectedKey, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Generated artifact {Id} has an unexpected storage key; retained for reconciliation",
                    row.Id);
                continue;
            }
            try
            {
                if (await _storage.DeleteAsync(row.storageKey, ct))
                    removed.Add(row);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Generated artifact cleanup delete failed for {Id}", row.Id);
            }
        }
        if (removed.Count > 0)
        {
            _db.aiGeneratedArtifacts.RemoveRange(removed);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch
            {
                // Failed removals must not linger in the tracker for a later
                // audit save to replay; the rows stay and the next pass retries.
                DetachOwned(removed);
                throw;
            }
        }
        return removed.Count;
    }

    /// <summary>Read-only metadata for the actor/store; foreign and absent share one code.</summary>
    internal async Task<AiGeneratedArtifact> LoadArtifactAsync(
        GeneratedFileImportRequest request, int storeId, int userId, CancellationToken ct)
    {
        var artifact = await _db.aiGeneratedArtifacts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.ArtifactId
                                      && a.storeId == storeId && a.userId == userId, ct);
        if (artifact == null || artifact.state != StateReady)
            throw new GeneratedArtifactException("ai_artifact_not_found");
        return artifact;
    }

    private void ValidateArtifactMatch(AiGeneratedArtifact artifact, GeneratedFileImportRequest request)
    {
        if (!string.Equals(artifact.sha256, request.Sha256, StringComparison.Ordinal)
            || artifact.length != request.Length
            || artifact.expiresAtUtc != request.ExpiresAtUtc)
            throw new GeneratedArtifactException("ai_artifact_mismatch");
        if (artifact.expiresAtUtc <= _time.GetUtcNow().UtcDateTime)
            throw new GeneratedArtifactException("ai_artifact_expired");
    }

    private async Task ValidateDestinationAsync(
        GeneratedFileImportRequest request, int storeId, CancellationToken ct)
    {
        if (request.Destination == GeneratedFileContract.DestinationStore)
        {
            if (request.FolderId is { } folderId
                && !await _db.storeFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == folderId && f.storeId == storeId, ct))
                throw new GeneratedArtifactException("ai_artifact_invalid_request");
            return;
        }

        var productOk = await _db.products.AsNoTracking()
            .AnyAsync(p => p.Id == request.ProductId
                           && p.storeId == storeId && p.deletedAt == null, ct);
        if (!productOk)
            throw new GeneratedArtifactException("ai_artifact_invalid_request");
        if (request.FolderId is { } productFolderId
            && !await _db.productFolders.AsNoTracking()
                .AnyAsync(f => f.Id == productFolderId && f.productId == request.ProductId, ct))
            throw new GeneratedArtifactException("ai_artifact_invalid_request");
    }

    private async Task<bool> CompletedTargetExistsAsync(
        AiGeneratedArtifactImport receipt, int fileId, int storeId, CancellationToken ct)
    {
        if (receipt.destination == GeneratedFileContract.DestinationStore)
        {
            return await _db.storeFiles.AsNoTracking()
                .AnyAsync(f => f.Id == fileId && f.storeId == storeId, ct);
        }
        return await _db.files.AsNoTracking()
            .AnyAsync(f => f.Id == fileId && f.productId == receipt.productId
                           && f.product.storeId == storeId, ct);
    }

    private async Task<GeneratedFileImportResult> ResolveCompletedAsync(
        AiGeneratedArtifactImport receipt,
        AiGeneratedArtifact artifact,
        int fileId,
        int storeId,
        CancellationToken ct)
    {
        if (!await CompletedTargetExistsAsync(receipt, fileId, storeId, ct))
            throw new GeneratedArtifactException("ai_artifact_import_unknown", outcomeUnknown: true);

        var downloadUrl = receipt.destination == GeneratedFileContract.DestinationStore
            ? $"/api/store-storage/files/{fileId}/download"
            : $"/api/files/{fileId}/download";
        return new GeneratedFileImportResult(
            receipt.destination, fileId,
            receipt.destination == GeneratedFileContract.DestinationStore ? null : receipt.productId,
            receipt.folderId,
            artifact.fileName, artifact.contentType, artifact.length, artifact.sha256,
            downloadUrl, Idempotent: true, WrittenCount: 0);
    }

    /// <summary>
    /// Known pre-commit cleanup: delete the owned destination object first, then
    /// remove only this newly claimed receipt so a fresh confirmed plan can retry.
    /// Returns false when anything could not be cleaned or persisted - the fence
    /// and reservation stay and the caller must report unknown; a cleanup failure
    /// never masks the original failure.
    /// </summary>
    private async Task<bool> ReleaseClaimAsync(
        string bindingId, string destinationKey, bool uploadAttempted)
    {
        try
        {
            if (uploadAttempted
                && !await _storage.DeleteAsync(destinationKey, CancellationToken.None))
                return false;
            var receipt = await _db.aiGeneratedArtifactImports
                .FirstOrDefaultAsync(r => r.Id == bindingId && r.state == ReceiptStarted,
                    CancellationToken.None);
            if (receipt == null)
                return true;
            _db.aiGeneratedArtifactImports.Remove(receipt);
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generated artifact import release of {BindingId} failed", bindingId);
            // A failed removal must not stay pending for the host audit save.
            foreach (var entry in _db.ChangeTracker.Entries<AiGeneratedArtifactImport>()
                         .Where(e => e.State == EntityState.Deleted).ToList())
                entry.State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>
    /// Best-effort capture cleanup: every key whose upload was ATTEMPTED is
    /// deleted regardless of the returned bool; only rows proven to have no
    /// object are removed. Rows behind a failed delete stay - they are the only
    /// quota/accounting record of the orphaned bytes and expire via the TTL pass.
    /// </summary>
    private async Task CleanupCaptureAsync(List<AiGeneratedArtifact> rows, List<string> attemptedKeys)
    {
        var attempted = new HashSet<string>(attemptedKeys, StringComparer.Ordinal);
        var removable = new List<AiGeneratedArtifact>();
        foreach (var row in rows)
        {
            if (!attempted.Contains(row.storageKey))
            {
                removable.Add(row); // the upload never ran; no object can exist
                continue;
            }
            try
            {
                if (await _storage.DeleteAsync(row.storageKey, CancellationToken.None))
                    removable.Add(row);
            }
            catch
            {
                // Deletion failed or threw: retain the row for TTL cleanup.
            }
        }
        if (removable.Count == 0)
            return;
        _db.aiGeneratedArtifacts.RemoveRange(removable);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Generated artifact capture cleanup save failed");
            DetachOwned(removable);
        }
    }

    /// <summary>
    /// SHA-256 of the canonical fixed-field binding: actor/store + exact artifact
    /// identity/hash/length/expiry + immutable filename/type + destination ids.
    /// </summary>
    private static string ComputeBindingId(
        GeneratedFileImportRequest request, AiGeneratedArtifact artifact, int storeId, int userId)
    {
        var tuple = new
        {
            v = 1,
            storeId,
            userId,
            artifactId = artifact.Id,
            artifact.sha256,
            artifact.length,
            expiresAtUtc = artifact.expiresAtUtc.ToString("O"),
            artifact.fileName,
            artifact.contentType,
            destination = request.Destination,
            productId = request.ProductId,
            folderId = request.FolderId,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tuple, BindingJson);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private async Task<AgentSession> ResolveActorAsync(int storeId, int userId, CancellationToken ct)
    {
        try
        {
            var session = await _sessions.ResolveForUserAsync(userId, storeId, ct);
            // An admin may resolve a numeric store it is not a member of; the store
            // must still exist.
            if (!await _db.stores.AsNoTracking().AnyAsync(s => s.Id == storeId, ct))
                throw new GeneratedArtifactException("ai_artifact_invalid_request");
            return session;
        }
        catch (UnauthorizedAccessException)
        {
            throw new GeneratedArtifactException("forbidden");
        }
    }

    private static void RequireFilesManage(AgentSession session)
    {
        if (!session.Permissions.Contains(Permissions.AiUse, StringComparer.OrdinalIgnoreCase)
            || !AgentAccess.CanMutate(session.Permissions, Permissions.FilesManage))
            throw new GeneratedArtifactException("forbidden");
    }

    /// <summary>Store-row lock serializing same-store reservations; MySQL only.</summary>
    private async Task LockStoreRowAsync(int storeId, CancellationToken ct)
    {
        if (!IsMySql())
            return;
        try
        {
            await _db.stores
                .FromSqlInterpolated($"SELECT * FROM stores WHERE Id = {storeId} FOR UPDATE")
                .AsNoTracking()
                .SingleAsync(ct);
        }
        catch (InvalidOperationException)
        {
            throw new GeneratedArtifactException("ai_artifact_invalid_request");
        }
    }

    private bool IsMySql() =>
        _db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

    private void EnsureNoAmbientTransaction()
    {
        if (_db.Database.CurrentTransaction != null)
            throw new InvalidOperationException(
                "Generated artifact operations do not run inside an ambient transaction.");
    }

    private void DetachOwned(IEnumerable<object?> entities)
    {
        // The scoped context may be reused for the host's audit save; owned
        // pending entities must not silently persist through it.
        var owned = new HashSet<object>(entities.Where(e => e != null)!);
        if (owned.Count == 0)
            return;
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
            if (owned.Contains(entry.Entity))
                entry.State = EntityState.Detached;
    }

    internal static async Task<(long Bytes, string Sha256)> CopyHashingAsync(
        Stream input, Stream output, long maxBytes, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var remaining = maxBytes - total;
            if (remaining <= 0)
                break;
            var read = await input.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
            if (read == 0)
                break;
            sha.TransformBlock(buffer, 0, read, null, 0);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }
        sha.TransformFinalBlock([], 0, 0);
        return (total, Convert.ToHexString(sha.Hash!).ToLowerInvariant());
    }

    private static void SetPrivateFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            System.IO.File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* best effort; the file is still process-private */ }
    }
}
