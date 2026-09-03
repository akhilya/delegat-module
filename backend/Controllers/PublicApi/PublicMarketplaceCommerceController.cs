using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Public API v1 — live marketplace prices, orders, labels, and promotions (pass-through, not stored).
/// </summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/connections/{connectionId:int}")]
[Tags("Marketplace commerce")]
public class PublicMarketplaceCommerceController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly MarketplaceCommerceService _commerce;

    public PublicMarketplaceCommerceController(
        PublicApiStoreContext storeCtx,
        MarketplaceCommerceService commerce)
    {
        _storeCtx = storeCtx;
        _commerce = commerce;
    }

    /// <summary>Reads current marketplace prices (not a 202 operation). Query <c>ids</c> is a comma-separated list of marketplace product ids.</summary>
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

        var externalIds = ParseIdList(ids);
        var prices = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.ImportPricesAsync(connection, externalIds, ct));

        return Ok(new { items = prices, total = prices.Count });
    }

    /// <summary>Lists orders from the marketplace cabinet. Items may include <c>imageUrl</c>.</summary>
    [HttpGet("orders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOrders(
        int storeId,
        int connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? cursor = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? channel = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        (page, pageSize) = PublicApiPaging.Normalize(page, pageSize);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrders, ct);

        var query = MarketplaceCommerceService.BuildOrderQuery(
            page, pageSize, cursor, status, from, to, channel, search);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetOrdersAsync(connection, query, ct));

        var items = result.Items ?? [];
        // Adapters may already set imageUrl (WB CDN, Ozon product/info). Local files fill the rest.
        await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, items, ct);
        return Ok(PublicApiResults.Page(
            items,
            page,
            pageSize,
            result.TotalCount ?? items.Count,
            result.NextCursor,
            result.HasMore));
    }

    /// <summary>Gets one marketplace order by id.</summary>
    [HttpGet("orders/{orderId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(
        int storeId,
        int connectionId,
        string orderId,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrders, ct);

        var order = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetOrderAsync(connection, orderId, ct));
        if (order == null)
            throw ApiException.NotFound("Order not found", code: "order_not_found");

        await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, [order], ct);
        return Ok(order);
    }

    /// <summary>
    /// Changes order status. Body: <c>action</c> (confirm, pack, ship, deliver, complete, cancel)
    /// plus optional <c>reason</c>, <c>trackingNumber</c>, <c>deliveryService</c>, <c>extra</c>.
    /// </summary>
    [HttpPost("orders/{orderId}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOrderStatus(
        int storeId,
        int connectionId,
        string orderId,
        [FromBody] OrderStatusUpdate? body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        if (body == null || string.IsNullOrWhiteSpace(body.Action))
            throw ApiException.BadRequest("action is required", code: "validation_error");

        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.UpdateOrderStatus, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.UpdateOrderStatusAsync(connection, orderId, body, ct));
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Order status update failed", code: "order_status_failed");

        if (result.Order != null)
            await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, [result.Order], ct);
        return Ok(result);
    }

    /// <summary>
    /// Downloads the packing label / barcode sticker for one order.
    /// Response: <c>contentType</c>, <c>fileName</c>, <c>contentBase64</c>, optional <c>barcode</c>, <c>format</c> (pdf/png/svg).
    /// Ozon FBS PDF, Wildberries PNG sticker, Yandex Market PDF. WooCommerce and OpenCart do not support this.
    /// </summary>
    [HttpGet("orders/{orderId}/label")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrderLabel(
        int storeId,
        int connectionId,
        string orderId,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrderLabels, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetOrderLabelAsync(connection, orderId, ct));
        if (!result.Success || result.Label == null)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Order label download failed", code: "order_label_failed");

        return Ok(result.Label);
    }

    /// <summary>Lists marketplace promotions, calendar campaigns, coupons, or auto-actions.</summary>
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

    /// <summary>Gets one promotion by marketplace id.</summary>
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

    /// <summary>Adds or removes products in a promotion. Body: <c>add</c> [{ externalProductId, actionPrice? }], <c>removeExternalProductIds</c>.</summary>
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
