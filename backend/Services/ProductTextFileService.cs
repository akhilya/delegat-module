using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using ProductFile = Delegat.Models.File;

namespace Delegat.Services;

/// <summary>
/// Saves a small, already-available text payload as one private product file.
/// JSON-callable counterpart of the multipart product upload: the same
/// product/folder ownership checks and shared store quotas, without form data.
/// The file stays private and is not attached to gallery or characteristics.
/// </summary>
public sealed class ProductTextFileService
{
    /// <summary>Strict UTF-8 byte cap for one from-text file.</summary>
    public const int MaxTextBytes = TextFileContent.MaxTextBytes;

    /// <summary>Cap on the JSON request envelope (escaped text plus fields).</summary>
    public const long MaxRequestBytes = TextFileContent.MaxRequestBytes;

    public const string DefaultContentType = TextFileContent.DefaultContentType;

    private readonly DataContext _db;
    private readonly IStorageService _storage;
    private readonly QuotaService _quota;

    public ProductTextFileService(DataContext db, IStorageService storage, QuotaService quota)
    {
        _db = db;
        _storage = storage;
        _quota = quota;
    }

    public sealed record Result(
        int Id,
        int ProductId,
        string FileName,
        string ContentType,
        long Length,
        int? FolderId,
        string DownloadUrl);

    /// <summary>
    /// Validates the request, stores the UTF-8 bytes under the product key
    /// convention, then persists the file row. Every rejection happens before
    /// the storage upload; a failed row save removes the uploaded object again.
    /// </summary>
    public async Task<Result> CreateFromTextAsync(
        int storeId,
        int userId,
        int productId,
        string? fileName,
        string? contentType,
        string? text,
        int? folderId,
        CancellationToken ct)
    {
        var productOk = await _db.products.AsNoTracking()
            .AnyAsync(p => p.Id == productId && p.storeId == storeId && p.deletedAt == null, ct);
        if (!productOk)
            throw ApiException.NotFound(code: "product_not_found");

        var content = TextFileContent.Create(fileName, contentType, text);

        if (folderId.HasValue)
        {
            var folderOk = await _db.productFolders.AsNoTracking()
                .AnyAsync(f => f.Id == folderId.Value && f.productId == productId, ct);
            if (!folderOk)
                throw ApiException.NotFound(code: "folder_not_found");
        }

        await _quota.EnsureFileQuotaAsync(storeId, 1);
        await _quota.EnsureStorageQuotaAsync(storeId, content.Bytes.Length);

        var path = $"{storeId}/{productId}/{Guid.NewGuid():N}/{content.FileName}";
        await using var stream = new MemoryStream(content.Bytes, writable: false);
        var uploaded = await _storage.UploadAsync(stream, path, content.Bytes.Length, content.ContentType, ct);
        if (!uploaded)
            throw ApiException.InternalServerError(code: "file_upload_failed");

        var entity = new ProductFile();
        entity.AddFill(new ProductFile.AddForm(
            path, userId, productId, content.ContentType, content.Bytes.Length));
        entity.folderId = folderId;
        entity.displayName = content.FileName;
        entity.fileType = FileType.Other;
        _db.files.Add(entity);
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
            productId,
            content.FileName,
            content.ContentType,
            content.Bytes.Length,
            folderId,
            $"/api/files/{entity.Id}/download");
    }
}
