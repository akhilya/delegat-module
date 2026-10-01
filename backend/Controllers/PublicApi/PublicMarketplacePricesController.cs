using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>Live marketplace prices.</summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/connections/{connectionId:int}")]
[Tags("Prices")]
public class PublicMarketplacePricesController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly MarketplaceCommerceService _commerce;

    public PublicMarketplacePricesController(PublicApiStoreContext storeCtx, MarketplaceCommerceService commerce)
    {
        _storeCtx = storeCtx;
        _commerce = commerce;
    }

    /// <summary>Lists current prices.</summary>
    /// <remarks>
    /// Live marketplace read, not a 202 job and not a stored snapshot.
    /// Query <c>ids</c> is a comma-separated list of marketplace product ids. Omit it to page whatever the adapter can list.
    /// </remarks>
    [HttpGet("prices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPrices(
        int storeId,
        int connectionId,
        [FromQuery] string? ids = null,
        CancellationToken ct = default)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ImportPrices, ct);

        var prices = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.ImportPricesAsync(connection, ParseIdList(ids), ct));

        return Ok(new { items = prices, total = prices.Count });
    }

    private static List<string>? ParseIdList(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return null;

        var list = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return list.Count == 0 ? null : list;
    }
}
