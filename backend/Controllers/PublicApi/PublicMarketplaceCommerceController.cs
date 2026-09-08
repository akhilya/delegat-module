using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Public API v1 — marketplace prices, stored order snapshots, live labels/shipments, and promotions.
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

    /// <summary>Lists stored order snapshots for the connection (eventually consistent). Query <c>page</c> / <c>pageSize</c>; numeric <c>cursor</c> is treated as a page number. Items may include <c>imageUrl</c>, <c>barcode</c>, and <c>syncedAt</c>.</summary>
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

    /// <summary>Gets one stored order by id. Missing if never synced → <c>404 order_not_found</c>.</summary>
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
        await _commerce.PersistOrderAsync(storeId, connection.Id, result.Order, ct);
        if (result.Order == null)
        {
            var fresh = await MarketplaceCommerceService.InvokeAsync(
                () => adapter.GetOrderAsync(connection, orderId, ct));
            await _commerce.PersistOrderAsync(storeId, connection.Id, fresh, ct);
        }
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

    /// <summary>Lists seller warehouses and marketplace offices. Query <c>kind</c>: <c>seller_warehouse</c> or <c>marketplace_office</c>.</summary>
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

    /// <summary>Lists live shipments / supplies. Query: <c>limit</c>, <c>cursor</c>, <c>openOnly</c>.</summary>
    [HttpGet("shipments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListShipments(
        int storeId,
        int connectionId,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        [FromQuery] bool openOnly = false,
        CancellationToken ct = default)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        if (limit < 1) limit = 50;
        if (limit > 200) limit = 200;
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var query = MarketplaceCommerceService.BuildShipmentQuery(limit, cursor, openOnly);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentsAsync(connection, query, ct),
            "shipment_failed");
        var items = result.Items ?? [];
        return Ok(PublicApiResults.Page(
            items,
            1,
            limit,
            result.TotalCount ?? items.Count,
            result.NextCursor,
            result.HasMore));
    }

    /// <summary>Creates an open shipment (Wildberries supply). Body: <c>name</c>, optional <c>destinationLocationId</c> (display only when the marketplace has no write).</summary>
    [HttpPost("shipments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateShipment(
        int storeId,
        int connectionId,
        [FromBody] CreateShipmentRequest? body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.CreateShipmentAsync(connection, body ?? new CreateShipmentRequest(), ct),
            "shipment_failed");
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Shipment create failed", code: "shipment_failed");
        return Ok(result);
    }

    /// <summary>Gets one shipment by marketplace id, including orders and boxes when the API has them.</summary>
    [HttpGet("shipments/{shipmentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShipment(
        int storeId,
        int connectionId,
        string shipmentId,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var shipment = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentAsync(connection, shipmentId, ct),
            "shipment_failed");
        if (shipment == null)
            throw ApiException.NotFound("Shipment not found", code: "shipment_not_found");
        return Ok(shipment);
    }

    /// <summary>Adds assembly orders to a shipment. Body: <c>orderIds</c>.</summary>
    [HttpPost("shipments/{shipmentId}/orders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddShipmentOrders(
        int storeId,
        int connectionId,
        string shipmentId,
        [FromBody] ShipmentOrdersUpdate? body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.AddOrdersToShipmentAsync(connection, shipmentId, body ?? new ShipmentOrdersUpdate(), ct),
            "shipment_failed");
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Could not add orders to the shipment", code: "shipment_failed");
        return Ok(result);
    }

    /// <summary>Lists shipping units (boxes) on a shipment.</summary>
    [HttpGet("shipments/{shipmentId}/boxes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListShipmentBoxes(
        int storeId,
        int connectionId,
        string shipmentId,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var items = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentBoxesAsync(connection, shipmentId, ct),
            "shipment_failed");
        return Ok(new { items, total = items.Count });
    }

    /// <summary>Creates shipping units on a shipment. Body: <c>amount</c> (default 1). Wildberries pickup-point supplies only.</summary>
    [HttpPost("shipments/{shipmentId}/boxes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateShipmentBoxes(
        int storeId,
        int connectionId,
        string shipmentId,
        [FromBody] CreateShipmentBoxesRequest? body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.CreateShipmentBoxesAsync(
                connection, shipmentId, body ?? new CreateShipmentBoxesRequest(), ct),
            "shipment_failed");
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Could not create shipment boxes", code: "shipment_failed");
        return Ok(result);
    }

    /// <summary>Puts assembly orders into a shipping unit. Body: <c>orderIds</c>.</summary>
    [HttpPost("shipments/{shipmentId}/boxes/{boxId}/orders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignShipmentBoxOrders(
        int storeId,
        int connectionId,
        string shipmentId,
        string boxId,
        [FromBody] ShipmentOrdersUpdate? body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.AssignOrdersToShipmentBoxAsync(
                connection, shipmentId, boxId, body ?? new ShipmentOrdersUpdate(), ct),
            "shipment_failed");
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Could not assign orders to the box", code: "shipment_failed");
        return Ok(result);
    }

    /// <summary>Downloads the QR sticker for one shipping unit. Same body as order labels.</summary>
    [HttpGet("shipments/{shipmentId}/boxes/{boxId}/label")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShipmentBoxLabel(
        int storeId,
        int connectionId,
        string shipmentId,
        string boxId,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentBoxLabelAsync(connection, shipmentId, boxId, ct),
            "shipment_failed");
        if (!result.Success || result.Label == null)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Box label download failed", code: "shipment_failed");
        return Ok(result.Label);
    }

    /// <summary>Downloads the shipment QR. Wildberries: only after the supply is handed over (<c>deliver</c>).</summary>
    [HttpGet("shipments/{shipmentId}/label")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetShipmentLabel(
        int storeId,
        int connectionId,
        string shipmentId,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);

        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentLabelAsync(connection, shipmentId, ct),
            "shipment_failed");
        if (!result.Success || result.Label == null)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Shipment label download failed", code: "shipment_failed");
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
