using System.Text.RegularExpressions;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

public sealed record BrandingSettingsDto(
    string name,
    string? description,
    string logoUrl,
    string faviconUrl,
    bool hasCustomLogo,
    bool hasCustomFavicon,
    string? primaryColor,
    string? accentColor,
    DateTime updatedAt);

public sealed class UpdateBrandingSettingsRequest
{
    public string? name { get; set; }
    public string? description { get; set; }
    public string? primaryColor { get; set; }
    public string? accentColor { get; set; }
    public bool? clearLogo { get; set; }
    public bool? clearFavicon { get; set; }
    /// <summary>When true, clear primary/accent back to design defaults.</summary>
    public bool? clearColors { get; set; }
}

public interface IBrandingSettingsService
{
    Task EnsureInitializedAsync(CancellationToken ct = default);

    Task<BrandingSettingsDto> GetAsync(CancellationToken ct = default);

    Task<BrandingSettingsDto> UpdateAsync(UpdateBrandingSettingsRequest request, CancellationToken ct = default);

    Task<BrandingSettingsDto> UploadLogoAsync(IFormFile file, CancellationToken ct = default);

    Task<BrandingSettingsDto> UploadFaviconAsync(IFormFile file, CancellationToken ct = default);

    Task<(Stream Stream, string ContentType)?> OpenLogoStreamAsync(CancellationToken ct = default);

    Task<(Stream Stream, string ContentType)?> OpenFaviconStreamAsync(CancellationToken ct = default);
}

/// <summary>
/// Cached instance branding. Seeded once from BRAND_* env; admin UI is source of truth after that.
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
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile BrandingSettingsDto _cache = new(
        name: InstanceBrandingSettings.DefaultName,
        description: null,
        logoUrl: "",
        faviconUrl: InstanceBrandingSettings.DefaultFaviconUrl,
        hasCustomLogo: false,
        hasCustomFavicon: false,
        primaryColor: null,
        accentColor: null,
        updatedAt: DateTime.UnixEpoch);

    private volatile bool _initialized;

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

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await GetAsync(ct);
    }

    public async Task<BrandingSettingsDto> GetAsync(CancellationToken ct = default)
    {
        if (_initialized) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized) return _cache;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await db.instanceBrandingSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == InstanceBrandingSettings.SingletonId, ct);

            if (row is null)
            {
                row = CreateSeedRow();
                db.instanceBrandingSettings.Add(row);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Seeded instance branding from env (name={Name})",
                    row.name);
            }

            _cache = ToDto(row);
            _initialized = true;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BrandingSettingsDto> UpdateAsync(
        UpdateBrandingSettingsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await EnsureRowTrackedAsync(db, ct);

            if (request.name is not null)
            {
                var trimmed = request.name.Trim();
                if (trimmed.Length == 0)
                    throw ApiException.BadRequest("Brand name cannot be empty.", code: "brand_name_required");
                if (trimmed.Length > 120)
                    throw ApiException.BadRequest("Brand name is too long.", code: "brand_name_too_long");
                row.name = trimmed;
            }

            if (request.description is not null)
            {
                var desc = request.description.Trim();
                row.description = desc.Length == 0 ? null : (desc.Length > 500 ? desc[..500] : desc);
            }

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
                row.faviconUrl = InstanceBrandingSettings.DefaultFaviconUrl;
            }

            row.updatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _cache = ToDto(row);
            _initialized = true;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BrandingSettingsDto> UploadLogoAsync(IFormFile file, CancellationToken ct = default)
        => UploadAssetAsync(file, isLogo: true, ct);

    public Task<BrandingSettingsDto> UploadFaviconAsync(IFormFile file, CancellationToken ct = default)
        => UploadAssetAsync(file, isLogo: false, ct);

    public async Task<(Stream Stream, string ContentType)?> OpenLogoStreamAsync(CancellationToken ct = default)
    {
        var dto = await GetAsync(ct);
        if (!dto.hasCustomLogo) return null;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var row = await db.instanceBrandingSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == InstanceBrandingSettings.SingletonId, ct);
        if (row?.logoObjectKey is null) return null;

        var stream = await _storage.GetStreamAsync(row.logoObjectKey, ct);
        if (stream is null) return null;
        return (stream, row.logoContentType ?? "application/octet-stream");
    }

    public async Task<(Stream Stream, string ContentType)?> OpenFaviconStreamAsync(CancellationToken ct = default)
    {
        var dto = await GetAsync(ct);
        if (!dto.hasCustomFavicon) return null;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var row = await db.instanceBrandingSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == InstanceBrandingSettings.SingletonId, ct);
        if (row?.faviconObjectKey is null) return null;

        var stream = await _storage.GetStreamAsync(row.faviconObjectKey, ct);
        if (stream is null) return null;
        return (stream, row.faviconContentType ?? "application/octet-stream");
    }

    private async Task<BrandingSettingsDto> UploadAssetAsync(IFormFile file, bool isLogo, CancellationToken ct)
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
            ? $"branding/logo{ext}"
            : $"branding/favicon{ext}";

        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();
            var row = await EnsureRowTrackedAsync(db, ct);

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
            await db.SaveChangesAsync(ct);

            _cache = ToDto(row);
            _initialized = true;
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    private InstanceBrandingSettings CreateSeedRow()
    {
        var name = Env("BRAND_NAME");
        if (string.IsNullOrWhiteSpace(name))
            name = InstanceBrandingSettings.DefaultName;

        var description = Env("BRAND_DESCRIPTION");
        var logoUrl = Env("BRAND_LOGO_URL");
        var faviconUrl = Env("BRAND_FAVICON_URL");
        var primary = TryNormalizeColor(Env("BRAND_PRIMARY"));
        var accent = TryNormalizeColor(Env("BRAND_ACCENT"));

        return new InstanceBrandingSettings
        {
            Id = InstanceBrandingSettings.SingletonId,
            name = name.Trim(),
            description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            logoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim(),
            faviconUrl = string.IsNullOrWhiteSpace(faviconUrl)
                ? InstanceBrandingSettings.DefaultFaviconUrl
                : faviconUrl.Trim(),
            primaryColor = primary,
            accentColor = accent,
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

    private async Task<InstanceBrandingSettings> EnsureRowTrackedAsync(DataContext db, CancellationToken ct)
    {
        var row = await db.instanceBrandingSettings
            .FirstOrDefaultAsync(x => x.Id == InstanceBrandingSettings.SingletonId, ct);
        if (row is not null)
            return row;

        row = CreateSeedRow();
        db.instanceBrandingSettings.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
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

    private static BrandingSettingsDto ToDto(InstanceBrandingSettings row)
    {
        var version = new DateTimeOffset(row.updatedAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(row.updatedAt, DateTimeKind.Utc)
            : row.updatedAt.ToUniversalTime()).ToUnixTimeSeconds();

        var hasCustomLogo = !string.IsNullOrWhiteSpace(row.logoObjectKey);
        var hasCustomFavicon = !string.IsNullOrWhiteSpace(row.faviconObjectKey);

        string logoUrl;
        if (hasCustomLogo)
            logoUrl = $"/api/settings/branding/assets/logo?v={version}";
        else if (IsLegacyOrEmptyLogo(row.logoUrl))
            logoUrl = "";
        else
            logoUrl = row.logoUrl!.Trim();

        string faviconUrl;
        if (hasCustomFavicon)
            faviconUrl = $"/api/settings/branding/assets/favicon?v={version}";
        else if (IsLegacyOrEmptyFavicon(row.faviconUrl))
            faviconUrl = InstanceBrandingSettings.DefaultFaviconUrl;
        else
            faviconUrl = row.faviconUrl!.Trim();

        return new BrandingSettingsDto(
            row.name,
            row.description,
            logoUrl,
            faviconUrl,
            hasCustomLogo,
            hasCustomFavicon,
            row.primaryColor,
            row.accentColor,
            row.updatedAt);
    }

    private static bool IsLegacyOrEmptyLogo(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || string.Equals(url.Trim(), InstanceBrandingSettings.LegacyDefaultLogoUrl, StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyOrEmptyFavicon(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || string.Equals(url.Trim(), InstanceBrandingSettings.LegacyDefaultFaviconUrl, StringComparison.OrdinalIgnoreCase);
}
