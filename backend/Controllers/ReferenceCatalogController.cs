using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Services.PlatformLink;

namespace Delegat.Controllers;

/// <summary>
/// Instance Reference Catalog API - reads from local DB (synced from Control Plane).
/// </summary>
[ApiController]
[JwtAuthorize]
[Route("/reference")]
public class ReferenceCatalogController : ControllerBase
{
    private readonly IReferenceCatalogService _catalog;

    public ReferenceCatalogController(IReferenceCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("currencies")]
    [RequiresPermission(Permissions.ReferenceRead)]
    public async Task<IActionResult> GetCurrencies(CancellationToken ct)
    {
        var items = await _catalog.GetCurrenciesAsync(ct);
        return Ok(items);
    }

    [HttpGet("timezones")]
    [RequiresPermission(Permissions.ReferenceRead)]
    public async Task<IActionResult> GetTimezones(CancellationToken ct)
    {
        var items = await _catalog.GetTimezonesAsync(ct);
        return Ok(items);
    }

    [HttpGet("plugins")]
    [RequiresPermission(Permissions.ReferenceRead)]
    public async Task<IActionResult> GetPlugins(CancellationToken ct)
    {
        if (!FeatureFlags.IsPluginsEnabled())
            return Ok(Array.Empty<object>());

        var items = await _catalog.GetPluginsAsync(ct);
        return Ok(items);
    }

    /// <summary>
    /// Status for future initialization wizard: whether catalogs are ready.
    /// </summary>
    [HttpGet("status")]
    [RequiresPermission(Permissions.ReferenceRead)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var status = await _catalog.GetStatusAsync(ct);
        return Ok(status);
    }
}
