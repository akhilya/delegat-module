using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>
/// Saves a small, already-available text payload as one private store file.
/// JSON-callable counterpart of the multipart upload: the same storage-key
/// convention, quota checks, and selected-store scoping, without form data.
/// </summary>
public sealed class StoreTextFileService
{
    /// <summary>Strict UTF-8 byte cap for one from-text file.</summary>
    public const int MaxTextBytes = TextFileContent.MaxTextBytes;

    /// <summary>Cap on the JSON request envelope (escaped text plus fields).</summary>
    public const long MaxRequestBytes = TextFileContent.MaxRequestBytes;

    public const string DefaultContentType = TextFileContent.DefaultContentType;

    private readonly DataContext _db;
    private readonly IStorageService _storage;
    private readonly QuotaService _quota;

    public StoreTextFileService(DataContext db, IStorageService storage, QuotaService quota)
    {
        _db = db;
        _storage = storage;
        _quota = quota;
    }

    public sealed record Result(
        int Id,
        string FileName,
        string ContentType,
        long Length,
        int? FolderId,
        string DownloadUrl);

    /// <summary>
    /// Validates the request, stores the UTF-8 bytes, then persists the store-file row.
    /// Every rejection happens before the storage upload; a failed row save removes the
    /// uploaded object again so no orphan remains.
    /// </summary>
    public async Task<Result> CreateFromTextAsync(
        int storeId,
        int userId,
        string? fileName,
        string? contentType,
        string? text,
        int? folderId,
        CancellationToken ct)
    {
        var content = TextFileContent.Create(fileName, contentType, text);

        if (folderId.HasValue)
        {
            var folderOk = await _db.storeFolders.AsNoTracking()
                .AnyAsync(f => f.Id == folderId.Value && f.storeId == storeId, ct);
            if (!folderOk)
                throw ApiException.NotFound(code: "folder_not_found");
        }

        await _quota.EnsureFileQuotaAsync(storeId, 1);
        await _quota.EnsureStorageQuotaAsync(storeId, content.Bytes.Length);

        var path = $"{storeId}/store/{Guid.NewGuid():N}/{content.FileName}";
        await using var stream = new MemoryStream(content.Bytes, writable: false);
        var uploaded = await _storage.UploadAsync(stream, path, content.Bytes.Length, content.ContentType, ct);
        if (!uploaded)
            throw ApiException.InternalServerError(code: "file_upload_failed");

        var entity = new StoreFile
        {
            folderId = folderId,
            displayName = content.FileName,
        };
        entity.Fill(path, userId, storeId, content.ContentType, content.Bytes.Length);
        _db.storeFiles.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            try
            {
                await _storage.DeleteAsync(path, CancellationToken.None);
            }
            catch
            {
                // The persistence failure is the one the caller must see.
            }
            throw;
        }

        return new Result(
            entity.Id,
            content.FileName,
            content.ContentType,
            content.Bytes.Length,
            folderId,
            $"/api/store-storage/files/{entity.Id}/download");
    }
}
