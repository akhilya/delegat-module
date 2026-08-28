using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Services;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class SettingsController : ControllerBase
{
    private readonly IDatabaseDumpService _databaseDumpService;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly IPublicApiSettingsService _publicApiSettings;
    private readonly IBrandingSettingsService _brandingSettings;
    private readonly ICurrentUserService _currentUser;
    private readonly QuotaService _quota;
    private readonly IOverageTrimService _overageTrim;
    private readonly IPlatformEntitlementsService _entitlements;

    public SettingsController(
        IDatabaseDumpService databaseDumpService,
        ILoggingSettingsService loggingSettings,
        IPublicApiSettingsService publicApiSettings,
        IBrandingSettingsService brandingSettings,
        ICurrentUserService currentUser,
        QuotaService quota,
        IOverageTrimService overageTrim,
        IPlatformEntitlementsService entitlements)
    {
        _databaseDumpService = databaseDumpService;
        _loggingSettings = loggingSettings;
        _publicApiSettings = publicApiSettings;
        _brandingSettings = brandingSettings;
        _currentUser = currentUser;
        _quota = quota;
        _overageTrim = overageTrim;
        _entitlements = entitlements;
    }

    /// <summary>
    /// Download a full MySQL logical dump (.sql). Administrators only.
    /// </summary>
    [HttpGet]
    [Route("/settings/database/dump")]
    [RequiresPermission(Permissions.SettingsDump)]
    public async Task<IActionResult> DownloadDatabaseDump(CancellationToken ct)
    {
        var handle = await _databaseDumpService.CreateDumpAsync(ct);
        HttpContext.Response.RegisterForDispose(handle);
        return PhysicalFile(handle.FilePath, "application/sql", handle.FileName);
    }

    /// <summary>Current instance logging toggles (admin UI).</summary>
    [HttpGet]
    [Route("/settings/logging")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<LoggingSettingsDto>> GetLoggingSettings(CancellationToken ct)
    {
        return Ok(await _loggingSettings.GetAsync(ct));
    }

    /// <summary>Update instance logging toggles. Takes effect immediately (no restart).</summary>
    [HttpPatch]
    [Route("/settings/logging")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<LoggingSettingsDto>> UpdateLoggingSettings(
        [FromBody] UpdateLoggingSettingsRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "Body required" });

        return Ok(await _loggingSettings.UpdateAsync(request, ct));
    }

    /// <summary>Current Public API enable toggle (admin UI).</summary>
    [HttpGet]
    [Route("/settings/public-api")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<PublicApiSettingsDto>> GetPublicApiSettings(CancellationToken ct)
    {
        return Ok(await _publicApiSettings.GetAsync(ct));
    }

    /// <summary>Enable or disable Public API. Takes effect immediately (no restart).</summary>
    [HttpPatch]
    [Route("/settings/public-api")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<PublicApiSettingsDto>> UpdatePublicApiSettings(
        [FromBody] UpdatePublicApiSettingsRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "Body required" });

        return Ok(await _publicApiSettings.UpdateAsync(request, ct));
    }

    /// <summary>
    /// Selected-store white-label branding when authenticated with a store;
    /// otherwise env defaults (auth pages before store selection).
    /// </summary>
    [HttpGet]
    [Route("/settings/branding")]
    [AllowAnonymous]
    [DisableRateLimiting]
    public async Task<ActionResult<BrandingSettingsDto>> GetBrandingSettings(CancellationToken ct)
    {
        if (_currentUser.IsAuthenticated)
        {
            var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
            if (storeId.HasValue)
                return Ok(await _brandingSettings.GetForStoreAsync(storeId.Value, ct));
        }

        return Ok(_brandingSettings.GetDefaults());
    }

    /// <summary>Update branding for the selected store. Takes effect immediately (no rebuild).</summary>
    [HttpPut]
    [Route("/settings/branding")]
    [RequiresPermission(Permissions.StoreSettingsManage)]
    public async Task<ActionResult<BrandingSettingsDto>> UpdateBrandingSettings(
        [FromBody] UpdateBrandingSettingsRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "Body required" });

        var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
        if (!storeId.HasValue)
            return BadRequest(new { error = "No store selected", code = "store_required" });

        return Ok(await _brandingSettings.UpdateAsync(storeId.Value, request, ct));
    }

    /// <summary>Upload custom logo for the selected store (PNG, JPEG, WebP, SVG, ICO; max 2 MB).</summary>
    [HttpPost]
    [Route("/settings/branding/logo")]
    [RequiresPermission(Permissions.StoreSettingsManage)]
    [RequestSizeLimit(BrandingSettingsService.MaxUploadBytes)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadBrandingLogo(
        IFormFile file,
        CancellationToken ct)
    {
        var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
        if (!storeId.HasValue)
            return BadRequest(new { error = "No store selected", code = "store_required" });

        return Ok(await _brandingSettings.UploadLogoAsync(storeId.Value, file, ct));
    }

    /// <summary>Upload custom favicon for the selected store (PNG, JPEG, WebP, SVG, ICO; max 2 MB).</summary>
    [HttpPost]
    [Route("/settings/branding/favicon")]
    [RequiresPermission(Permissions.StoreSettingsManage)]
    [RequestSizeLimit(BrandingSettingsService.MaxUploadBytes)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadBrandingFavicon(
        IFormFile file,
        CancellationToken ct)
    {
        var storeId = await _currentUser.GetSelectedStoreIdAsync(ct);
        if (!storeId.HasValue)
            return BadRequest(new { error = "No store selected", code = "store_required" });

        return Ok(await _brandingSettings.UploadFaviconAsync(storeId.Value, file, ct));
    }

    /// <summary>Stream custom logo bytes for a store (anonymous).</summary>
    [HttpGet]
    [Route("/settings/branding/assets/logo")]
    [AllowAnonymous]
    [DisableRateLimiting]
    // Do not use VaryByQueryKeys — it requires Response Caching middleware (not registered).
    // Client cache-busts via ?v=; Cache-Control is set below.
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetBrandingLogoAsset([FromQuery] int storeId, CancellationToken ct)
    {
        if (storeId <= 0)
            return BadRequest(new { error = "storeId required" });

        var opened = await _brandingSettings.OpenLogoStreamAsync(storeId, ct);
        if (opened is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(opened.Value.Stream, opened.Value.ContentType);
    }

    /// <summary>Stream custom favicon bytes for a store (anonymous).</summary>
    [HttpGet]
    [Route("/settings/branding/assets/favicon")]
    [AllowAnonymous]
    [DisableRateLimiting]
    // Do not use VaryByQueryKeys — it requires Response Caching middleware (not registered).
    // Client cache-busts via ?v=; Cache-Control is set below.
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetBrandingFaviconAsset([FromQuery] int storeId, CancellationToken ct)
    {
        if (storeId <= 0)
            return BadRequest(new { error = "storeId required" });

        var opened = await _brandingSettings.OpenFaviconStreamAsync(storeId, ct);
        if (opened is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(opened.Value.Stream, opened.Value.ContentType);
    }

    /// <summary>Plan quotas vs current usage (per store and rollup).</summary>
    [HttpGet]
    [Route("/settings/quotas")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<QuotaUsageSnapshotDto>> GetQuotas(CancellationToken ct)
    {
        var entitlements = await _entitlements.GetCachedAsync(ct);
        var snapshot = await _quota.GetUsageSnapshotAsync(
            entitlements.Linked,
            entitlements.PlanName,
            entitlements.OveragePurgeAt,
            ct);
        return Ok(snapshot);
    }

    /// <summary>Deterministic preview of data that will be permanently trimmed when over quota.</summary>
    [HttpGet]
    [Route("/settings/quotas/overage-preview")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<OverageTrimPreviewDto>> GetOveragePreview(CancellationToken ct)
    {
        return Ok(await _overageTrim.PreviewAsync(ct));
    }

    /// <summary>Apply overage trim now (same selection as preview / auto worker).</summary>
    [HttpPost]
    [Route("/settings/quotas/trim")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<OverageTrimResultDto>> TrimOverage(CancellationToken ct)
    {
        var preview = await _overageTrim.PreviewAsync(ct);
        if (!preview.IsOverQuota)
            return Ok(new OverageTrimResultDto(0, 0, 0, 0));

        return Ok(await _overageTrim.ExecuteAsync(ct));
    }
}
