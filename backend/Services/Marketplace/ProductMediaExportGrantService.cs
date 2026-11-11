using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

/// <summary>Creates and revokes permanent, connection-scoped URLs for selected product videos.</summary>
public sealed class ProductMediaExportGrantService
{
    public const int YandexMaxVideos = 6;
    private const string PublicBaseUrlSetting = "PUBLIC_BASE_URL";

    private readonly DataContext _db;
    private readonly IConfiguration _configuration;
    private readonly IPublicMediaExportBaseUrlValidator _baseUrlValidator;
    private readonly IStorageService _storage;

    public ProductMediaExportGrantService(
        DataContext db,
        IConfiguration configuration,
        IPublicMediaExportBaseUrlValidator baseUrlValidator,
        IStorageService storage)
    {
        _db = db;
        _configuration = configuration;
        _baseUrlValidator = baseUrlValidator;
        _storage = storage;
    }

    /// <summary>
    /// Issues or reuses stable URLs for the requested videos. Call only immediately before
    /// sending an actual adapter export, never during readiness checks or mapping previews.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetOrCreateUrlsAsync(
        int connectionId,
        int productId,
        IReadOnlyList<int> requestedFileIds,
        CancellationToken ct = default)
    {
        var fileIds = requestedFileIds.Distinct().ToArray();
        if (fileIds.Length == 0)
            return [];
        if (fileIds.Length > YandexMaxVideos)
            throw new ApiException(
                "Yandex Market accepts at most six video URLs per product.",
                StatusCodes.Status400BadRequest,
                code: "media_export_video_limit_exceeded");

        var rawBaseUrl = _configuration[PublicBaseUrlSetting]?.Trim();
        if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out var baseUri)
            || !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || baseUri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new ApiException(
                "Set PUBLIC_BASE_URL to the externally reachable HTTPS origin before exporting videos.",
                StatusCodes.Status503ServiceUnavailable,
                code: string.IsNullOrWhiteSpace(rawBaseUrl)
                    ? "media_export_public_url_required"
                    : "media_export_public_url_invalid");
        }

        try
        {
            await _baseUrlValidator.ValidateAsync(baseUri, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ApiException(
                "PUBLIC_BASE_URL must resolve to a publicly reachable HTTPS origin.",
                StatusCodes.Status503ServiceUnavailable,
                innerException: ex,
                code: "media_export_public_url_not_public");
        }

        var connection = await _db.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        var productStoreId = await _db.products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => (int?)p.storeId)
            .FirstOrDefaultAsync(ct);
        if (connection == null || productStoreId != connection.storeId
            || !string.Equals(connection.adapterKey, "yandex", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.BadRequest(
                "The video grant does not match a Yandex Market connection and product in the same store.",
                code: "media_export_connection_mismatch");
        }

        var files = await _db.files
            .Where(f => f.productId == productId && fileIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);
        if (files.Count != fileIds.Length)
            throw ApiException.NotFound(code: "media_export_file_not_found");

        foreach (var fileId in fileIds)
        {
            var file = files[fileId];
            if (string.IsNullOrWhiteSpace(file.contentType)
                || !file.contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.BadRequest(code: "media_export_file_not_video");
            }
            if (!await _storage.ExistsAsync(file.filePath, ct))
                throw ApiException.NotFound(code: "media_export_file_not_found");
        }

        var existing = await _db.productMediaExportGrants
            .Where(g => g.connectionId == connectionId && fileIds.Contains(g.fileId))
            .ToDictionaryAsync(g => g.fileId, ct);
        var changed = new List<ProductMediaExportGrant>();
        foreach (var fileId in fileIds)
        {
            if (existing.TryGetValue(fileId, out var grant))
            {
                if (grant.revokedAt.HasValue)
                {
                    grant.token = NewToken();
                    grant.revision = NewRevision();
                    grant.revokedAt = null;
                    changed.Add(grant);
                }
            }
            else
            {
                grant = new ProductMediaExportGrant
                {
                    fileId = fileId,
                    connectionId = connectionId,
                    token = NewToken(),
                    revision = NewRevision(),
                    createdAt = DateTime.UtcNow
                };
                _db.productMediaExportGrants.Add(grant);
                existing.Add(fileId, grant);
                changed.Add(grant);
            }
        }

        if (changed.Count > 0)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrent first exports can race on the unique file+connection key.
                // Reuse the winner's persisted URL if every requested grant is now active.
                foreach (var entry in _db.ChangeTracker.Entries<ProductMediaExportGrant>().ToList())
                    entry.State = EntityState.Detached;

                var winners = await _db.productMediaExportGrants.AsNoTracking()
                    .Where(g => g.connectionId == connectionId && fileIds.Contains(g.fileId) && g.revokedAt == null)
                    .ToDictionaryAsync(g => g.fileId, ct);
                if (winners.Count != fileIds.Length)
                    throw;
                existing = winners;
            }
        }

        return fileIds.Select(fileId => BuildUrl(baseUri, existing[fileId].token)).ToArray();
    }

    public async Task<int> RevokeForConnectionAsync(int connectionId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var grants = await _db.productMediaExportGrants
            .Where(g => g.connectionId == connectionId && g.revokedAt == null)
            .ToListAsync(ct);
        foreach (var grant in grants)
        {
            grant.revokedAt = now;
            grant.revision = NewRevision();
        }
        if (grants.Count > 0)
            await _db.SaveChangesAsync(ct);
        return grants.Count;
    }

    private static string BuildUrl(Uri baseUri, string token)
        => new Uri(baseUri, $"/api/pub/media-export/{token}").AbsoluteUri;

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private static string NewRevision() => Guid.NewGuid().ToString("N");
}
