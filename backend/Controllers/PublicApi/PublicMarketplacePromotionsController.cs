using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>Campaigns, coupons, and product enrollment.</summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/connections/{connectionId:int}")]
[Tags("Promotions")]
public class PublicMarketplacePromotionsController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly MarketplaceCommerceService _commerce;

    public PublicMarketplacePromotionsController(PublicApiStoreContext storeCtx, MarketplaceCommerceService commerce)
    {
        _storeCtx = storeCtx;
        _commerce = commerce;
    }

    /// <summary>Lists promotions.</summary>
    /// <remarks>Query <c>page</c>, <c>pageSize</c>, <c>cursor</c>, <c>status</c>, <c>type</c>. Marketplace actions, calendar campaigns, or shop coupons. Not stored.</remarks>
    [HttpGet("promotions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPromotions(
        int storeId,
        int connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? cursor = null,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        (page, pageSize) = PublicApiPaging.Normalize(page, pageSize);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetPromotions, ct);

        var query = MarketplaceCommerceService.BuildPromotionQuery(page, pageSize, cursor, status, type);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetPromotionsAsync(connection, query, ct));

        var items = result.Items ?? [];
        return Ok(PublicApiResults.Page(
            items,
            page,
            pageSize,
            result.TotalCount ?? items.Count,
            result.NextCursor,
            result.HasMore));
    }

    /// <summary>Gets one promotion.</summary>
    /// <remarks>Missing → <c>404 promotion_not_found</c>.</remarks>
    [HttpGet("promotions/{promotionId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPromotion(
        int storeId,
        int connectionId,
        string promotionId,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetPromotions, ct);

        var promo = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetPromotionAsync(connection, promotionId, ct));
        if (promo == null)
            throw ApiException.NotFound("Promotion not found", code: "promotion_not_found");

        return Ok(promo);
    }

    /// <summary>Adds or removes promotion products.</summary>
    /// <remarks>Body: <c>add</c> [{ <c>externalProductId</c>, <c>actionPrice</c>? }], <c>removeExternalProductIds</c>. Failure → <c>400 promotion_update_failed</c>.</remarks>
    [HttpPost("promotions/{promotionId}/products")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePromotionProducts(
        int storeId,
        int connectionId,
        string promotionId,
        [FromBody] PromotionProductsUpdate? body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var update = body ?? new PromotionProductsUpdate();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.UpdatePromotions, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.UpdatePromotionProductsAsync(connection, promotionId, update, ct));
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Promotion update failed", code: "promotion_update_failed");

        return Ok(result);
    }
}
