using Delegat.Enums;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Security;
using Delegat.Infrastructure.Storage;
using Delegat.Models.Ai;
using Delegat.Services;
using Microsoft.EntityFrameworkCore;
using ProductFile = Delegat.Models.File;

namespace Delegat.Services.Ai.Agent;

public sealed class AgentFileUploadService
{
    public const int MaxChatAttachments = 500;
    public const string ChatAttachPrefix = "__ai-chat-attach/";
    public static readonly TimeSpan ChatAttachTtl = TimeSpan.FromHours(24);

    public static DateTime LiveCutoffUtc(DateTime? now = null) =>
        (now ?? DateTime.UtcNow) - ChatAttachTtl;

    public static bool IsLive(DateTime createdAt, DateTime? now = null) =>
        createdAt >= LiveCutoffUtc(now);

    private readonly DataContext _db;
    private readonly IUnitOfWork _uow;
    private readonly IStorageService _storage;
    private readonly QuotaService _quota;

    public AgentFileUploadService(
        DataContext db,
        IUnitOfWork uow,
        IStorageService storage,
        QuotaService quota)
    {
        _db = db;
        _uow = uow;
        _storage = storage;
        _quota = quota;
    }

    public async Task<IReadOnlyList<AgentUploadedFile>> UploadProductFilesAsync(
        int storeId,
        int userId,
        int productId,
        IReadOnlyList<IFormFile> files,
        int? folderId,
        CancellationToken ct)
    {
        if (files.Count == 0)
            throw new InvalidOperationException("files are required");

        var product = await _db.products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == storeId && p.deletedAt == null, ct)
            ?? throw new InvalidOperationException("product_not_found");

        await _quota.EnsureFileQuotaAsync(storeId, files.Count);
        await _quota.EnsureStorageQuotaAsync(storeId, files.Sum(f => f.Length));

        if (folderId.HasValue)
        {
            var folderOk = await _db.productFolders.AsNoTracking()
                .AnyAsync(f => f.Id == folderId.Value && f.productId == product.Id, ct);
            if (!folderOk)
                throw new InvalidOperationException("folder_not_found");
        }

        var created = new List<AgentUploadedFile>();
        var repo = _uow.Repository<ProductFile>();
        foreach (var file in files)
        {
            var uniqueKey = Guid.NewGuid().ToString("N");
            var safeName = FileNameSafety.Sanitize(file.FileName);
            var filePath = $"{storeId}/{productId}/{uniqueKey}/{safeName}";
            var entity = new ProductFile();
            entity.AddFill(new ProductFile.AddForm(filePath, userId, productId, file.ContentType ?? "application/octet-stream", file.Length));
            entity.folderId = folderId;
            entity.displayName = safeName;
            entity.fileType = file.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
                ? FileType.Image
                : FileType.Other;

            await using var stream = file.OpenReadStream();
            var uploaded = await _storage.UploadAsync(stream, filePath, file.Length, file.ContentType ?? "application/octet-stream", ct);
            if (!uploaded)
                throw new InvalidOperationException($"Failed to upload {file.FileName}");

            await repo.AddWithoutSaveAsync(entity);
            created.Add(new AgentUploadedFile { Id = entity.Id, FileName = safeName, Length = file.Length });
        }

        await _uow.SaveChangesAsync(ct);
        return created;
    }

    public async Task<IReadOnlyList<StagedChatAttachment>> StageChatAttachmentsAsync(
        int storeId,
        int userId,
        string sessionId,
        IReadOnlyList<IFormFile> files,
        IReadOnlyList<string>? relativePaths,
        CancellationToken ct)
    {
        sessionId = (sessionId ?? "").Trim();
        if (sessionId.Length is 0 or > 64)
            throw new InvalidOperationException("session_id_required");
        if (files.Count == 0)
            throw new InvalidOperationException("files are required");

        var existing = await _db.aiChatAttachments.CountAsync(
            a => a.sessionId == sessionId && a.storeId == storeId && a.userId == userId, ct);
        if (existing + files.Count > MaxChatAttachments)
            throw new InvalidOperationException("too_many_attachments");

        await _quota.EnsureStorageQuotaAsync(storeId, files.Sum(f => f.Length));

        var rows = new List<AiChatAttachment>();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var hinted = i < (relativePaths?.Count ?? 0) ? relativePaths![i] : file.FileName;
            var relativePath = SanitizeRelativePath(hinted, file.FileName)
                ?? throw new InvalidOperationException("invalid_relative_path");
            var safeName = FileNameSafety.Sanitize(file.FileName);
            var uniqueKey = Guid.NewGuid().ToString("N");
            var storageKey = $"{ChatAttachPrefix}{storeId}/{sessionId}/{uniqueKey}/{safeName}";
            var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

            await using var stream = file.OpenReadStream();
            var uploaded = await _storage.UploadAsync(stream, storageKey, file.Length, contentType, ct);
            if (!uploaded)
                throw new InvalidOperationException($"Failed to upload {file.FileName}");

            var row = new AiChatAttachment
            {
                sessionId = sessionId,
                storeId = storeId,
                userId = userId,
                relativePath = relativePath,
                fileName = safeName,
                contentType = contentType.Length > 200 ? contentType[..200] : contentType,
                length = file.Length,
                storageKey = storageKey,
                createdAt = DateTime.UtcNow,
            };
            _db.aiChatAttachments.Add(row);
            rows.Add(row);
        }

        await _db.SaveChangesAsync(ct);
        return rows.Select(a => new StagedChatAttachment
        {
            Id = a.Id,
            RelativePath = a.relativePath,
            FileName = a.fileName,
            Length = a.length,
            ExpiresAt = a.createdAt + ChatAttachTtl,
        }).ToList();
    }

    public async Task<IReadOnlyList<AgentUploadedFile>> AssignChatAttachmentsToProductAsync(
        int storeId,
        int userId,
        int productId,
        IReadOnlyList<int> attachmentIds,
        int? folderId,
        CancellationToken ct)
    {
        var ids = attachmentIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("attachmentIds are required");

        var product = await _db.products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == storeId && p.deletedAt == null, ct)
            ?? throw new InvalidOperationException("product_not_found");

        var attachments = await _db.aiChatAttachments
            .Where(a => a.storeId == storeId && a.userId == userId && ids.Contains(a.Id))
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        if (attachments.Count != ids.Count)
            throw new InvalidOperationException("attachment_not_found");
        if (attachments.Any(a => !IsLive(a.createdAt)))
            throw new InvalidOperationException("attachment_expired");

        await _quota.EnsureFileQuotaAsync(storeId, attachments.Count);

        if (folderId.HasValue)
        {
            var folderOk = await _db.productFolders.AsNoTracking()
                .AnyAsync(f => f.Id == folderId.Value && f.productId == product.Id, ct);
            if (!folderOk)
                throw new InvalidOperationException("folder_not_found");
        }

        var created = new List<AgentUploadedFile>();
        var repo = _uow.Repository<ProductFile>();
        var stagingKeys = new List<string>();
        foreach (var attachment in attachments)
        {
            var uniqueKey = Guid.NewGuid().ToString("N");
            var safeName = FileNameSafety.Sanitize(attachment.fileName);
            var filePath = $"{storeId}/{productId}/{uniqueKey}/{safeName}";
            var copied = await _storage.CopyAsync(attachment.storageKey, filePath, ct);
            if (!copied)
                throw new InvalidOperationException($"Failed to assign {attachment.fileName}");

            var entity = new ProductFile();
            entity.AddFill(new ProductFile.AddForm(
                filePath, userId, productId, attachment.contentType, attachment.length));
            entity.folderId = folderId;
            entity.displayName = safeName;
            entity.fileType = attachment.contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? FileType.Image
                : FileType.Other;

            await repo.AddWithoutSaveAsync(entity);
            created.Add(new AgentUploadedFile { Id = entity.Id, FileName = safeName, Length = attachment.length });
            stagingKeys.Add(attachment.storageKey);
            _db.aiChatAttachments.Remove(attachment);
        }

        await _uow.SaveChangesAsync(ct);
        foreach (var key in stagingKeys)
            await _storage.DeleteAsync(key, ct);
        return created;
    }

    public static string? SanitizeRelativePath(string? raw, string fileName)
    {
        var s = (raw ?? fileName).Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(s))
            s = Path.GetFileName(fileName) ?? "file";
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p is "." or ".."))
            return null;
        s = string.Join('/', parts);
        if (s.Length > 500)
            s = s[..500];
        return s.Length == 0 ? null : s;
    }

    public async Task<AiChatAttachment?> GetLiveOwnedAsync(
        int storeId, int userId, int attachmentId, CancellationToken ct)
    {
        var row = await _db.aiChatAttachments.AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == attachmentId && a.storeId == storeId && a.userId == userId, ct);
        if (row == null || !IsLive(row.createdAt))
            return null;
        return row;
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct)
    {
        var cutoff = LiveCutoffUtc();
        var expired = await _db.aiChatAttachments
            .Where(a => a.createdAt < cutoff)
            .ToListAsync(ct);
        if (expired.Count == 0)
            return 0;

        var keys = expired.Select(a => a.storageKey).Distinct().ToList();
        _db.aiChatAttachments.RemoveRange(expired);
        await _db.SaveChangesAsync(ct);
        foreach (var key in keys)
            await _storage.DeleteAsync(key, ct);
        return expired.Count;
    }
}
