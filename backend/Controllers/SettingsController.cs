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

    public SettingsController(
        IDatabaseDumpService databaseDumpService,
        ILoggingSettingsService loggingSettings,
        IPublicApiSettingsService publicApiSettings,
        IBrandingSettingsService brandingSettings)
    {
        _databaseDumpService = databaseDumpService;
        _loggingSettings = loggingSettings;
        _publicApiSettings = publicApiSettings;
        _brandingSettings = brandingSettings;
    }

    /// <summary>
    /// Download a full MySQL logical dump (.sql). Administrators only.
    /// </summary>
    [HttpGet]
    [Route("/settings/database/dump")]
    [RequiresPermission(Permissions.SettingsPlatform)]
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

    /// <summary>Current instance white-label branding (public — used on auth and shell before login).</summary>
    [HttpGet]
    [Route("/settings/branding")]
    [AllowAnonymous]
    [DisableRateLimiting]
    public async Task<ActionResult<BrandingSettingsDto>> GetBrandingSettings(CancellationToken ct)
    {
        return Ok(await _brandingSettings.GetAsync(ct));
    }

    /// <summary>Update branding identity and colors. Takes effect immediately (no rebuild).</summary>
    [HttpPut]
    [Route("/settings/branding")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<ActionResult<BrandingSettingsDto>> UpdateBrandingSettings(
        [FromBody] UpdateBrandingSettingsRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "Body required" });

        return Ok(await _brandingSettings.UpdateAsync(request, ct));
    }

    /// <summary>Upload custom logo (PNG, JPEG, WebP, SVG, ICO; max 2 MB).</summary>
    [HttpPost]
    [Route("/settings/branding/logo")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    [RequestSizeLimit(BrandingSettingsService.MaxUploadBytes)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadBrandingLogo(
        IFormFile file,
        CancellationToken ct)
    {
        return Ok(await _brandingSettings.UploadLogoAsync(file, ct));
    }

    /// <summary>Upload custom favicon (PNG, JPEG, WebP, SVG, ICO; max 2 MB).</summary>
    [HttpPost]
    [Route("/settings/branding/favicon")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    [RequestSizeLimit(BrandingSettingsService.MaxUploadBytes)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadBrandingFavicon(
        IFormFile file,
        CancellationToken ct)
    {
        return Ok(await _brandingSettings.UploadFaviconAsync(file, ct));
    }

    /// <summary>Stream custom logo bytes (anonymous).</summary>
    [HttpGet]
    [Route("/settings/branding/assets/logo")]
    [AllowAnonymous]
    [DisableRateLimiting]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetBrandingLogoAsset(CancellationToken ct)
    {
        var opened = await _brandingSettings.OpenLogoStreamAsync(ct);
        if (opened is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(opened.Value.Stream, opened.Value.ContentType);
    }

    /// <summary>Stream custom favicon bytes (anonymous).</summary>
    [HttpGet]
    [Route("/settings/branding/assets/favicon")]
    [AllowAnonymous]
    [DisableRateLimiting]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetBrandingFaviconAsset(CancellationToken ct)
    {
        var opened = await _brandingSettings.OpenFaviconStreamAsync(ct);
        if (opened is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(opened.Value.Stream, opened.Value.ContentType);
    }
}
