using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

public sealed record BrandingSettingsDto(
    string name,
    string logoUrl,
    string faviconUrl,
    bool hasCustomLogo,
    bool hasCustomFavicon,
    string? primaryColor,
    string? accentColor,
    DateTime updatedAt,
    int? storeId = null);

public sealed class UpdateBrandingSettingsRequest
{
    public string? name { get; set; }
    public string? primaryColor { get; set; }
    public string? accentColor { get; set; }
    public bool? clearLogo { get; set; }
    public bool? clearFavicon { get; set; }
    /// <summary>When true, clear primary/accent back to design defaults.</summary>
    public bool? clearColors { get; set; }
}

public interface IBrandingSettingsService
{
    /// <summary>No-op kept for boot parity with other settings warmers.</summary>
    Task EnsureInitializedAsync(CancellationToken ct = default);

    /// <summary>Env/Vite defaults when no store is selected (auth pages).</summary>
    BrandingSettingsDto GetDefaults();

    Task<BrandingSettingsDto> GetForStoreAsync(int storeId, CancellationToken ct = default);

    Task<BrandingSettingsDto> UpdateAsync(int storeId, UpdateBrandingSettingsRequest request, CancellationToken ct = default);

    Task<BrandingSettingsDto> UploadLogoAsync(int storeId, IFormFile file, CancellationToken ct = default);

    Task<BrandingSettingsDto> UploadFaviconAsync(int storeId, IFormFile file, CancellationToken ct = default);

    Task<(Stream Stream, string ContentType)?> OpenLogoStreamAsync(int storeId, CancellationToken ct = default);

    Task<(Stream Stream, string ContentType)?> OpenFaviconStreamAsync(int storeId, CancellationToken ct = default);

    /// <summary>Delete branding row and MinIO objects for a store being removed.</summary>
    Task DeleteForStoreAsync(int storeId, CancellationToken ct = default);

    /// <summary>Drop in-memory branding cache for a store (e.g. after rename).</summary>
    void InvalidateStoreCache(int storeId);

    /// <summary>Favicon URL for list UIs (sidebar switcher). Does not seed missing rows.</summary>
    Task<IReadOnlyDictionary<int, string>> GetFaviconUrlsAsync(
        IEnumerable<int> storeIds,
        CancellationToken ct = default);
}

/// <summary>
/// Per-store branding with in-memory cache keyed by store id.
/// </summary>
public sealed class BrandingSettingsService : IBrandingSettingsService
{
    public const long MaxUploadBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/svg+xml",
        "image/x-icon",
        "image/vnd.microsoft.icon",
    };

    private static readonly Regex HexColor = new(
        @"^#([0-9A-Fa-f]{6})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStorageService _storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BrandingSettingsService> _logger;
    private readonly ConcurrentDictionary<int, BrandingSettingsDto> _cache = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    public BrandingSettingsService(
        IServiceScopeFactory scopeFactory,
        IStorageService storage,
        IConfiguration configuration,
        ILogger<BrandingSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _storage = storage;
        _configuration = configuration;
        _logger = logger;
    }

    public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;

    public BrandingSettingsDto GetDefaults() => CreateDefaultsDto();

    public async Task<IReadOnlyDictionary<int, string>> GetFaviconUrlsAsync(
        IEnumerable<int> storeIds,
        CancellationToken ct = default)
    {
        var ids = storeIds.Distinct().Where(id => id > 0).ToList();
        if (ids.Count == 0)
            return new Dictionary<int, string>();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var rows = await db.storeBrandingSettings
            .AsNoTracking()
            .Where(b => ids.Contains(b.StoreId))
            .ToListAsync(ct);

        var byId = rows.ToDictionary(r => r.StoreId);
        var result = new Dictionary<int, string>(ids.Count);
        foreach (var id in ids)
        {
            byId.TryGetValue(id, out var row);
            result[id] = ResolveFaviconUrl(id, row);
        }

        return result;
    }

    /// <summary>Resolve list/display favicon without seeding a branding row.</summary>
    public static string ResolveFaviconUrl(int storeId, StoreBrandingSettings? row)
    {
        if (row is null)
            return StoreBrandingSettings.DefaultFaviconUrl;

        if (!string.IsNullOrWhiteSpace(row.faviconObjectKey))
        {
            var version = new DateTimeOffset(row.updatedAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(row.updatedAt, DateTimeKind.Utc)
                : row.updatedAt.ToUniversalTime()).ToUnixTimeSeconds();
            return $"/api/settings/branding/assets/favicon?storeId={storeId}&v={version}";
        }

        if (IsLegacyOrEmptyFavicon(row.faviconUrl))
            return StoreBrandingSettings.DefaultFaviconUrl;

        return row.faviconUrl!.Trim();
    }

    public void InvalidateStoreCache(int storeId) => _cache.TryRemove(storeId, out _);

    public async Task<BrandingSettingsDto> GetForStoreAsync(int storeId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(storeId, out var cached))
        {
            var refreshed = await WithCurrentStoreNameAsync(storeId, cached, ct);
            if (!ReferenceEquals(refreshed, cached))
                _cache[storeId] = refreshed;
            return refreshed;
        }

        var gate = GateFor(storeId);
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(storeId, out cached))
            {
                var refreshed = await WithCurrentStoreNameAsync(storeId, cached, ct);
                if (!ReferenceEquals(refreshed, cached))
                    _cache[storeId] = refreshed;
                return refreshed;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await EnsureRowTrackedAsync(db, storeId, ct);
            if (await SyncRowNameFromStoreAsync(db, row, storeId, ct))
                await db.SaveChangesAsync(ct);
            var dto = ToDto(row);
            _cache[storeId] = dto;
            return dto;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<BrandingSettingsDto> UpdateAsync(
        int storeId,
        UpdateBrandingSettingsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gate = GateFor(storeId);
        await gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await EnsureRowTrackedAsync(db, storeId, ct);

            if (request.clearColors == true)
            {
                row.primaryColor = null;
                row.accentColor = null;
            }
            else
            {
                if (request.primaryColor is not null)
                    row.primaryColor = NormalizeColorOrNull(request.primaryColor);
                if (request.accentColor is not null)
                    row.accentColor = NormalizeColorOrNull(request.accentColor);
            }

            // Brand display name tracks the store name (no separate editable brand name).
            await SyncRowNameFromStoreAsync(db, row, storeId, ct);

            if (request.clearLogo == true)
            {
                await TryDeleteObjectAsync(row.logoObjectKey, ct);
                row.logoObjectKey = null;
                row.logoContentType = null;
                row.logoUrl = null;
            }

            if (request.clearFavicon == true)
            {
                await TryDeleteObjectAsync(row.faviconObjectKey, ct);
                row.faviconObjectKey = null;
                row.faviconContentType = null;
                row.faviconUrl = StoreBrandingSettings.DefaultFaviconUrl;
            }

            row.updatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var dto = ToDto(row);
            _cache[storeId] = dto;
            return dto;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<BrandingSettingsDto> UploadLogoAsync(int storeId, IFormFile file, CancellationToken ct = default)
        => UploadAssetAsync(storeId, file, isLogo: true, ct);

    public Task<BrandingSettingsDto> UploadFaviconAsync(int storeId, IFormFile file, CancellationToken ct = default)
        => UploadAssetAsync(storeId, file, isLogo: false, ct);

    public async Task<(Stream Stream, string ContentType)?> OpenLogoStreamAsync(int storeId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var row = await db.storeBrandingSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
        if (row?.logoObjectKey is null) return null;

        var stream = await _storage.GetStreamAsync(row.logoObjectKey, ct);
        if (stream is null) return null;
        return (stream, row.logoContentType ?? "application/octet-stream");
    }

    public async Task<(Stream Stream, string ContentType)?> OpenFaviconStreamAsync(int storeId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var row = await db.storeBrandingSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
        if (row?.faviconObjectKey is null) return null;

        var stream = await _storage.GetStreamAsync(row.faviconObjectKey, ct);
        if (stream is null) return null;
        return (stream, row.faviconContentType ?? "application/octet-stream");
    }

    public async Task DeleteForStoreAsync(int storeId, CancellationToken ct = default)
    {
        var gate = GateFor(storeId);
        await gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await db.storeBrandingSettings
                .FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
            if (row is not null)
            {
                await TryDeleteObjectAsync(row.logoObjectKey, ct);
                await TryDeleteObjectAsync(row.faviconObjectKey, ct);
                db.storeBrandingSettings.Remove(row);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                // Row may already be cascade-deleted with the store — still purge MinIO keys.
                try
                {
                    var keys = await _storage.ListObjectsByPrefixAsync($"branding/{storeId}/", ct);
                    foreach (var key in keys)
                        await TryDeleteObjectAsync(key, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to list branding objects for store {StoreId}", storeId);
                }
            }

            _cache.TryRemove(storeId, out _);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BrandingSettingsDto> UploadAssetAsync(
        int storeId,
        IFormFile file,
        bool isLogo,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw ApiException.BadRequest("File is required.", code: "file_required");
        if (file.Length > MaxUploadBytes)
            throw ApiException.BadRequest("File exceeds the 2 MB limit.", code: "file_too_large");

        var contentType = (file.ContentType ?? "").Trim();
        if (!AllowedContentTypes.Contains(contentType))
            throw ApiException.BadRequest(
                "Unsupported image type. Use PNG, JPEG, WebP, SVG, or ICO.",
                code: "unsupported_image_type");

        var ext = ExtensionForContentType(contentType);
        var objectKey = isLogo
            ? $"branding/{storeId}/logo{ext}"
            : $"branding/{storeId}/favicon{ext}";

        var gate = GateFor(storeId);
        await gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await EnsureRowTrackedAsync(db, storeId, ct);

            var previousKey = isLogo ? row.logoObjectKey : row.faviconObjectKey;

            await using (var stream = file.OpenReadStream())
            {
                var ok = await _storage.UploadAsync(stream, objectKey, file.Length, contentType, ct);
                if (!ok)
                    throw ApiException.InternalServerError("Failed to store branding asset.");
            }

            if (!string.IsNullOrEmpty(previousKey)
                && !string.Equals(previousKey, objectKey, StringComparison.Ordinal))
            {
                await TryDeleteObjectAsync(previousKey, ct);
            }

            if (isLogo)
            {
                row.logoObjectKey = objectKey;
                row.logoContentType = contentType;
                row.logoUrl = null;
            }
            else
            {
                row.faviconObjectKey = objectKey;
                row.faviconContentType = contentType;
                row.faviconUrl = null;
            }

            row.updatedAt = DateTime.UtcNow;
            await SyncRowNameFromStoreAsync(db, row, storeId, ct);
            await db.SaveChangesAsync(ct);

            var dto = ToDto(row);
            _cache[storeId] = dto;
            return dto;
        }
        finally
        {
            gate.Release();
        }
    }

    private BrandingSettingsDto CreateDefaultsDto()
    {
        var name = Env("BRAND_NAME");
        if (string.IsNullOrWhiteSpace(name))
            name = StoreBrandingSettings.DefaultName;

        var logoUrl = Env("BRAND_LOGO_URL");
        var faviconUrl = Env("BRAND_FAVICON_URL");
        var primary = TryNormalizeColor(Env("BRAND_PRIMARY"));
        var accent = TryNormalizeColor(Env("BRAND_ACCENT"));

        return new BrandingSettingsDto(
            name: name.Trim(),
            logoUrl: IsLegacyOrEmptyLogo(logoUrl) ? "" : logoUrl!.Trim(),
            faviconUrl: IsLegacyOrEmptyFavicon(faviconUrl)
                ? StoreBrandingSettings.DefaultFaviconUrl
                : faviconUrl!.Trim(),
            hasCustomLogo: false,
            hasCustomFavicon: false,
            primaryColor: primary,
            accentColor: accent,
            updatedAt: DateTime.UnixEpoch,
            storeId: null);
    }

    private StoreBrandingSettings CreateSeedRow(int storeId, string? storeName)
    {
        var defaults = CreateDefaultsDto();
        var name = string.IsNullOrWhiteSpace(storeName)
            ? defaults.name
            : storeName.Trim();
        if (name.Length > 120)
            name = name[..120];

        return new StoreBrandingSettings
        {
            StoreId = storeId,
            name = name,
            logoUrl = string.IsNullOrWhiteSpace(defaults.logoUrl) ? null : defaults.logoUrl,
            faviconUrl = defaults.faviconUrl,
            primaryColor = defaults.primaryColor,
            accentColor = defaults.accentColor,
            updatedAt = DateTime.UtcNow,
        };
    }

    private string? Env(string key)
    {
        var fromConfig = _configuration[key];
        if (!string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig;
        return Environment.GetEnvironmentVariable(key);
    }

    private async Task<StoreBrandingSettings> EnsureRowTrackedAsync(
        DataContext db,
        int storeId,
        CancellationToken ct)
    {
        var row = await db.storeBrandingSettings
            .FirstOrDefaultAsync(x => x.StoreId == storeId, ct);
        if (row is not null)
            return row;

        var store = await db.stores
            .AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => new { s.Id, s.name })
            .FirstOrDefaultAsync(ct);
        if (store is null)
            throw ApiException.NotFound("Store not found", code: "store_not_found");

        row = CreateSeedRow(storeId, store.name);
        db.storeBrandingSettings.Add(row);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Seeded store branding (storeId={StoreId}, name={Name})",
            storeId,
            row.name);
        return row;
    }

    private SemaphoreSlim GateFor(int storeId) =>
        _gates.GetOrAdd(storeId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Overlay DTO name with current <c>stores.name</c>.</summary>
    private async Task<BrandingSettingsDto> WithCurrentStoreNameAsync(
        int storeId,
        BrandingSettingsDto dto,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var storeName = await db.stores
            .AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => s.name)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(storeName))
            return dto;

        var trimmed = storeName.Trim();
        if (trimmed.Length > 120)
            trimmed = trimmed[..120];

        if (string.Equals(dto.name, trimmed, StringComparison.Ordinal))
            return dto;

        return dto with { name = trimmed };
    }

    /// <summary>Returns true when the branding row name was updated.</summary>
    private static async Task<bool> SyncRowNameFromStoreAsync(
        DataContext db,
        StoreBrandingSettings row,
        int storeId,
        CancellationToken ct)
    {
        var storeName = await db.stores
            .AsNoTracking()
            .Where(s => s.Id == storeId)
            .Select(s => s.name)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(storeName))
            return false;

        var trimmed = storeName.Trim();
        if (trimmed.Length > 120)
            trimmed = trimmed[..120];

        if (string.Equals(row.name, trimmed, StringComparison.Ordinal))
            return false;

        row.name = trimmed;
        return true;
    }

    private async Task TryDeleteObjectAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            await _storage.DeleteAsync(key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete branding object {Key}", key);
        }
    }

    private static string? TryNormalizeColor(string? value)
    {
        try
        {
            return NormalizeColorOrNull(value ?? "");
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private static string? NormalizeColorOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 6 && !trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;
        if (!HexColor.IsMatch(trimmed))
            throw ApiException.BadRequest("Color must be #RRGGBB.", code: "invalid_color");
        return trimmed.ToUpperInvariant();
    }

    private static string ExtensionForContentType(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        _ => ".bin",
    };

    private static BrandingSettingsDto ToDto(StoreBrandingSettings row)
    {
        var version = new DateTimeOffset(row.updatedAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(row.updatedAt, DateTimeKind.Utc)
            : row.updatedAt.ToUniversalTime()).ToUnixTimeSeconds();

        var hasCustomLogo = !string.IsNullOrWhiteSpace(row.logoObjectKey);
        var hasCustomFavicon = !string.IsNullOrWhiteSpace(row.faviconObjectKey);

        string logoUrl;
        if (hasCustomLogo)
            logoUrl = $"/api/settings/branding/assets/logo?storeId={row.StoreId}&v={version}";
        else if (IsLegacyOrEmptyLogo(row.logoUrl))
            logoUrl = "";
        else
            logoUrl = row.logoUrl!.Trim();

        string faviconUrl;
        if (hasCustomFavicon)
            faviconUrl = $"/api/settings/branding/assets/favicon?storeId={row.StoreId}&v={version}";
        else if (IsLegacyOrEmptyFavicon(row.faviconUrl))
            faviconUrl = StoreBrandingSettings.DefaultFaviconUrl;
        else
            faviconUrl = row.faviconUrl!.Trim();

        return new BrandingSettingsDto(
            row.name,
            logoUrl,
            faviconUrl,
            hasCustomLogo,
            hasCustomFavicon,
            row.primaryColor,
            row.accentColor,
            row.updatedAt,
            row.StoreId);
    }

    private static bool IsLegacyOrEmptyLogo(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || string.Equals(url.Trim(), StoreBrandingSettings.LegacyDefaultLogoUrl, StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyOrEmptyFavicon(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || string.Equals(url.Trim(), StoreBrandingSettings.LegacyDefaultFaviconUrl, StringComparison.OrdinalIgnoreCase);
}
