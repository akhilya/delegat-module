using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>Stored orders, seller status changes, and packing labels.</summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/connections/{connectionId:int}")]
[Tags("Orders")]
public class PublicMarketplaceOrdersController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly MarketplaceCommerceService _commerce;

    public PublicMarketplaceOrdersController(PublicApiStoreContext storeCtx, MarketplaceCommerceService commerce)
    {
        _storeCtx = storeCtx;
        _commerce = commerce;
    }

    /// <summary>Lists stored orders.</summary>
    /// <remarks>
    /// Snapshot for this connection (eventually consistent), not a live marketplace call.
    /// Query <c>page</c>, <c>pageSize</c>, <c>status</c>, <c>from</c>, <c>to</c>, <c>channel</c> (<c>fbs</c> / <c>fbo</c> / <c>shop</c>), <c>search</c>.
    /// A numeric <c>cursor</c> is the next page number. Items may include <c>imageUrl</c>, <c>barcode</c>, and <c>syncedAt</c>.
    /// Never synced → an empty page.
    /// </remarks>
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
        var (connection, _) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrders, ct);

        var query = MarketplaceCommerceService.BuildOrderQuery(
            page, pageSize, cursor, status, from, to, channel, search);
        var (items, total, hasMore, nextCursor) = await _commerce.ListStoredConnectionOrdersAsync(
            connection, query, ct);

        await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, items, ct);
        return Ok(PublicApiResults.Page(
            items,
            query.Page,
            pageSize,
            total,
            nextCursor,
            hasMore));
    }

    /// <summary>Gets one stored order.</summary>
    /// <remarks>Missing if it was never synced → <c>404 order_not_found</c>. Ozon accepts the posting number or the cabinet order number.</remarks>
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

        var order = await _commerce.GetStoredOrderAsync(
            connection, adapter, orderId, liveFallback: false, ct);
        if (order == null)
            throw ApiException.NotFound(code: "order_not_found");

        await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, [order], ct);
        return Ok(order);
    }

    /// <summary>Changes an order’s seller status.</summary>
    /// <remarks>
    /// Body: <c>action</c> (<c>confirm</c>, <c>pack</c>, <c>ship</c>, <c>deliver</c>, <c>complete</c>, <c>cancel</c>)
    /// plus optional <c>reason</c>, <c>trackingNumber</c>, <c>deliveryService</c>, <c>extra</c>.
    /// Not every marketplace accepts every action. Failure → <c>400 order_status_failed</c>.
    /// </remarks>
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
            throw ApiException.BadRequest(code: "order_action_required");

        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.UpdateOrderStatus, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.UpdateOrderStatusAsync(connection, orderId, body, ct));
        if (!result.Success)
            throw ApiException.BadRequest(code: "order_status_failed");

        await _commerce.FinishSellerStatusAsync(
            connection, adapter, orderId, body.Action, result, ct);
        return Ok(result);
    }

    /// <summary>Downloads a packing label.</summary>
    /// <remarks>
    /// <c>contentType</c>, <c>fileName</c>, <c>contentBase64</c>, optional <c>barcode</c>, <c>format</c> (<c>pdf</c> / <c>png</c> / <c>svg</c>).
    /// Ozon FBS PDF, Wildberries PNG sticker, Yandex Market PDF. Only in statuses where that marketplace can print.
    /// WooCommerce and OpenCart → <c>400 adapter_capability_missing</c>. Wrong status → <c>400 order_label_failed</c>.
    /// </remarks>
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
            throw ApiException.BadRequest(code: "order_label_failed");

        return Ok(result.Label);
    }
}
