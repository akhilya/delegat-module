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
    public const int WildberriesMaxVideos = 1;
    public const long WildberriesMaxVideoBytes = 50L * 1024 * 1024;
    private const string PublicBaseUrlSetting = "PUBLIC_BASE_URL";

    private sealed record GrantPolicy(
        int? MaxVideos,
        long? MaxBytes,
        IReadOnlySet<string>? AcceptedContentTypes);

    private static readonly IReadOnlyDictionary<string, GrantPolicy> GrantPolicies =
        new Dictionary<string, GrantPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            ["yandex"] = new(YandexMaxVideos, null, null),
            // Ozon accepts stable external video URLs in its complex video attributes.
            // No count, byte-size, or MIME limit is imposed here until its public contract
            // documents a seller API limit.
            ["ozon"] = new(null, null, null),
            ["wildberries"] = new(
                WildberriesMaxVideos,
                WildberriesMaxVideoBytes,
                new HashSet<string>(["video/mp4", "video/quicktime"], StringComparer.OrdinalIgnoreCase))
        };

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
            || !GrantPolicies.TryGetValue(connection.adapterKey, out var policy))
        {
            throw ApiException.BadRequest(
                "The video grant does not match a supported marketplace connection and product in the same store.",
                code: "media_export_connection_mismatch");
        }

        if (policy.MaxVideos is int maxVideos && fileIds.Length > maxVideos)
            throw new ApiException(
                $"{connection.adapterKey} accepts at most {maxVideos} video(s) per product.",
                StatusCodes.Status400BadRequest,
                code: "media_export_video_limit_exceeded");

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
            if (policy.AcceptedContentTypes is { Count: > 0 }
                && !policy.AcceptedContentTypes.Contains(file.contentType.Trim()))
            {
                throw ApiException.BadRequest(code: "media_export_video_type_not_supported");
            }
            if (policy.MaxBytes is long maxBytes && file.length > maxBytes)
            {
                throw ApiException.BadRequest(code: "media_export_video_size_exceeded");
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

        return fileIds.Select(fileId => BuildUrl(
            baseUri,
            existing[fileId].token,
            connection.adapterKey,
            files[fileId].contentType)).ToArray();
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

    private static string BuildUrl(Uri baseUri, string token, string adapterKey, string contentType)
    {
        // WB requires direct file URLs to end in a filename and extension. The endpoint
        // authenticates by the preceding opaque token; this suffix is only a file hint.
        var fileName = string.Equals(adapterKey, "wildberries", StringComparison.OrdinalIgnoreCase)
            ? contentType.Equals("video/quicktime", StringComparison.OrdinalIgnoreCase)
                ? "video.mov"
                : "video.mp4"
            : null;
        var suffix = fileName == null ? "" : $"/{fileName}";
        return new Uri(baseUri, $"/api/pub/media-export/{token}{suffix}").AbsoluteUri;
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private static string NewRevision() => Guid.NewGuid().ToString("N");
}
