using System.Text;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
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
    public const int MaxTextBytes = 256 * 1024; // 262144

    /// <summary>Cap on the JSON request envelope (escaped text plus fields).</summary>
    public const long MaxRequestBytes = 2 * 1024 * 1024;

    public const string DefaultContentType = "text/plain";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/csv",
        "application/json",
    };

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
        if (string.IsNullOrWhiteSpace(fileName))
            throw ApiException.BadRequest(code: "file_name_required");
        if (text == null)
            throw ApiException.BadRequest(code: "text_required");

        var safeName = FileNameSafety.Sanitize(fileName);
        var type = NormalizeContentType(contentType)
            ?? throw ApiException.BadRequest(code: "content_type_not_allowed");

        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxTextBytes)
            throw ApiException.BadRequest(code: "text_too_large");

        if (folderId.HasValue)
        {
            var folderOk = await _db.storeFolders.AsNoTracking()
                .AnyAsync(f => f.Id == folderId.Value && f.storeId == storeId, ct);
            if (!folderOk)
                throw ApiException.NotFound(code: "folder_not_found");
        }

        await _quota.EnsureFileQuotaAsync(storeId, 1);
        await _quota.EnsureStorageQuotaAsync(storeId, bytes.Length);

        var path = $"{storeId}/store/{Guid.NewGuid():N}/{safeName}";
        await using var stream = new MemoryStream(bytes, writable: false);
        var uploaded = await _storage.UploadAsync(stream, path, bytes.Length, type, ct);
        if (!uploaded)
            throw ApiException.InternalServerError(code: "file_upload_failed");

        var entity = new StoreFile
        {
            folderId = folderId,
            displayName = safeName,
        };
        entity.Fill(path, userId, storeId, type, bytes.Length);
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
            safeName,
            type,
            bytes.Length,
            folderId,
            $"/api/store-storage/files/{entity.Id}/download");
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return DefaultContentType;

        var type = contentType.Split(';', 2)[0].Trim();
        return AllowedContentTypes.Contains(type) ? type.ToLowerInvariant() : null;
    }
}
