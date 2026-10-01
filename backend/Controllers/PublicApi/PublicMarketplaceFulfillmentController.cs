using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>Seller warehouses and drop-off offices.</summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/connections/{connectionId:int}")]
[Tags("Fulfillment")]
public class PublicMarketplaceFulfillmentController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly MarketplaceCommerceService _commerce;

    public PublicMarketplaceFulfillmentController(PublicApiStoreContext storeCtx, MarketplaceCommerceService commerce)
    {
        _storeCtx = storeCtx;
        _commerce = commerce;
    }

    /// <summary>Lists warehouses and offices.</summary>
    /// <remarks>
    /// Query <c>kind</c>: <c>seller_warehouse</c> or <c>marketplace_office</c>. Omit it for both.
    /// Not stored. Unsupported adapter → <c>400 adapter_capability_missing</c>.
    /// </remarks>
    [HttpGet("fulfillment/locations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFulfillmentLocations(
        int storeId,
        int connectionId,
        [FromQuery] string? kind = null,
        CancellationToken ct = default)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetFulfillmentLocations, ct);

        var items = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetFulfillmentLocationsAsync(
                connection, new FulfillmentLocationQuery { Kind = kind }, ct),
            "fulfillment_location_failed");
        return Ok(new { items, total = items.Count });
    }
}
