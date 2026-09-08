using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

/// <summary>
/// JWT cabinet API for marketplace prices, orders, labels, shipments, and promotions.
/// Orders, labels, and shipments need <c>orders.manage</c>; prices and promotions need <c>marketplace.manage</c>.
/// Order lists and gets read stored snapshots; seller actions still go to the marketplace.
/// </summary>
[ApiController]
[JwtAuthorize]
public class MarketplaceCommerceController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly MarketplaceCommerceService _commerce;
    private readonly MarketplaceOrderSyncService _orderSync;

    public MarketplaceCommerceController(
        ICurrentUserService currentUser,
        MarketplaceCommerceService commerce,
        MarketplaceOrderSyncService orderSync)
    {
        _currentUser = currentUser;
        _commerce = commerce;
        _orderSync = orderSync;
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/prices")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ListPrices(
        int connectionId,
        [FromQuery] string? ids = null,
        CancellationToken ct = default)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ImportPrices, ct);

        List<string>? externalIds = null;
        if (!string.IsNullOrWhiteSpace(ids))
        {
            externalIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        var prices = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.ImportPricesAsync(connection, externalIds, ct));
        return Ok(new { items = prices, total = prices.Count });
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/orders")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> ListOrders(
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
        var storeId = await RequireStoreAsync();
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var (connection, _) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrders, ct);
        var query = MarketplaceCommerceService.BuildOrderQuery(
            page, pageSize, cursor, status, from, to, channel, search);
        var (items, total, hasMore, nextCursor) = await _commerce.ListStoredConnectionOrdersAsync(
            connection, query, ct);
        await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, items, ct);
        var mailboxCounts = await _commerce.CountStoredMailboxesAsync(connection, query, ct);
        return Ok(new
        {
            items,
            page = query.Page,
            pageSize,
            total,
            hasMore,
            nextCursor,
            mailboxCounts
        });
    }

    [HttpGet("/marketplace/orders")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> ListStoreOrders(
        [FromQuery] string tab = "assembly",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? channel = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var storeId = await RequireStoreAsync();
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;
        var query = MarketplaceCommerceService.BuildOrderQuery(
            page, pageSize, null, null, from, to, channel, search);
        var (items, total, hasMore, nextCursor) = await _commerce.ListStoredStoreTabAsync(
            storeId, tab, query, ct);
        foreach (var group in items.GroupBy(i => i.ConnectionId))
        {
            var orders = group.Select(i => i.Order).ToList();
            await _commerce.AttachLocalOrderItemImagesAsync(group.Key, orders, ct);
        }

        var tabCounts = await _commerce.CountStoredOverviewTabsAsync(storeId, query, ct);
        return Ok(new
        {
            items,
            page,
            pageSize,
            total,
            hasMore,
            nextCursor,
            tabCounts
        });
    }

    [HttpPost("/marketplace/orders/sync")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> SyncStoreOrders(
        [FromQuery] string tab = "assembly",
        CancellationToken ct = default)
    {
        var storeId = await RequireStoreAsync();
        var result = await _orderSync.SyncStoreTabAsync(storeId, tab, "page", ct);
        return Ok(new
        {
            pulled = result.Pulled,
            skipped = result.Skipped,
            upserted = result.Upserted,
            reason = result.Reason
        });
    }

    [HttpPost("/marketplace/connections/{connectionId:int}/orders/sync")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> SyncConnectionOrders(
        int connectionId,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var storeId = await RequireStoreAsync();
        var (connection, _) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrders, ct);
        IReadOnlyList<string> statuses = string.IsNullOrWhiteSpace(status)
            ? MarketplaceOrderBuckets.ListStatusesForAdapter(connection.adapterKey)
            : [status];
        var result = await _orderSync.SyncConnectionStatusesAsync(
            connection, statuses, "page", ct);
        return Ok(new
        {
            pulled = result.Pulled,
            skipped = result.Skipped,
            upserted = result.Upserted,
            reason = result.Reason
        });
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/orders/{orderId}")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetOrder(int connectionId, string orderId, CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrders, ct);
        var order = await MarketplaceCommerceService.InvokeAsync(
            () => _commerce.GetStoredOrderAsync(connection, adapter, orderId, liveFallback: true, ct));
        if (order == null)
            throw ApiException.NotFound("Order not found", code: "order_not_found");
        await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, [order], ct);
        return Ok(order);
    }

    [HttpPost("/marketplace/connections/{connectionId:int}/orders/{orderId}/status")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdateOrderStatus(
        int connectionId,
        string orderId,
        [FromBody] OrderStatusUpdate? body,
        CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
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

    [HttpGet("/marketplace/connections/{connectionId:int}/orders/{orderId}/label")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetOrderLabel(int connectionId, string orderId, CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrderLabels, ct);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetOrderLabelAsync(connection, orderId, ct));
        if (!result.Success || result.Label == null)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Order label download failed", code: "order_label_failed");
        return Ok(result.Label);
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/fulfillment/locations")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> ListFulfillmentLocations(
        int connectionId,
        [FromQuery] string? kind = null,
        CancellationToken ct = default)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetFulfillmentLocations, ct);
        var items = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetFulfillmentLocationsAsync(
                connection, new FulfillmentLocationQuery { Kind = kind }, ct),
            "fulfillment_location_failed");
        return Ok(new { items, total = items.Count });
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/shipments")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> ListShipments(
        int connectionId,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        [FromQuery] bool openOnly = false,
        CancellationToken ct = default)
    {
        var storeId = await RequireStoreAsync();
        if (limit < 1) limit = 50;
        if (limit > 200) limit = 200;
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);
        var query = MarketplaceCommerceService.BuildShipmentQuery(limit, cursor, openOnly);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentsAsync(connection, query, ct),
            "shipment_failed");
        var items = result.Items ?? [];
        return Ok(new
        {
            items,
            total = result.TotalCount ?? items.Count,
            hasMore = result.HasMore,
            nextCursor = result.NextCursor
        });
    }

    [HttpPost("/marketplace/connections/{connectionId:int}/shipments")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateShipment(
        int connectionId,
        [FromBody] CreateShipmentRequest? body,
        CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.CreateShipmentAsync(connection, body ?? new CreateShipmentRequest(), ct),
            "shipment_failed");
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Shipment create failed", code: "shipment_failed");
        return Ok(result);
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/shipments/{shipmentId}")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetShipment(int connectionId, string shipmentId, CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);
        var shipment = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentAsync(connection, shipmentId, ct),
            "shipment_failed");
        if (shipment == null)
            throw ApiException.NotFound("Shipment not found", code: "shipment_not_found");
        return Ok(shipment);
    }

    [HttpPost("/marketplace/connections/{connectionId:int}/shipments/{shipmentId}/orders")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> AddShipmentOrders(
        int connectionId,
        string shipmentId,
        [FromBody] ShipmentOrdersUpdate? body,
        CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.AddOrdersToShipmentAsync(connection, shipmentId, body ?? new ShipmentOrdersUpdate(), ct),
            "shipment_failed");
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Could not add orders to the shipment", code: "shipment_failed");
        return Ok(result);
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/shipments/{shipmentId}/boxes")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> ListShipmentBoxes(int connectionId, string shipmentId, CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);
        var items = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentBoxesAsync(connection, shipmentId, ct),
            "shipment_failed");
        return Ok(new { items, total = items.Count });
    }

    [HttpPost("/marketplace/connections/{connectionId:int}/shipments/{shipmentId}/boxes")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> CreateShipmentBoxes(
        int connectionId,
        string shipmentId,
        [FromBody] CreateShipmentBoxesRequest? body,
        CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
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

    [HttpPost("/marketplace/connections/{connectionId:int}/shipments/{shipmentId}/boxes/{boxId}/orders")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> AssignShipmentBoxOrders(
        int connectionId,
        string shipmentId,
        string boxId,
        [FromBody] ShipmentOrdersUpdate? body,
        CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
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

    [HttpGet("/marketplace/connections/{connectionId:int}/shipments/{shipmentId}/boxes/{boxId}/label")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetShipmentBoxLabel(
        int connectionId,
        string shipmentId,
        string boxId,
        CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentBoxLabelAsync(connection, shipmentId, boxId, ct),
            "shipment_failed");
        if (!result.Success || result.Label == null)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Box label download failed", code: "shipment_failed");
        return Ok(result.Label);
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/shipments/{shipmentId}/label")]
    [RequiresPermission(Permissions.OrdersManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetShipmentLabel(int connectionId, string shipmentId, CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.ManageShipments, ct);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetShipmentLabelAsync(connection, shipmentId, ct),
            "shipment_failed");
        if (!result.Success || result.Label == null)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Shipment label download failed", code: "shipment_failed");
        return Ok(result.Label);
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/promotions")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> ListPromotions(
        int connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? cursor = null,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {
        var storeId = await RequireStoreAsync();
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetPromotions, ct);
        var query = MarketplaceCommerceService.BuildPromotionQuery(page, pageSize, cursor, status, type);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetPromotionsAsync(connection, query, ct));

        var items = result.Items ?? [];
        return Ok(new
        {
            items,
            page,
            pageSize,
            total = result.TotalCount ?? items.Count,
            hasMore = result.HasMore,
            nextCursor = result.NextCursor
        });
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/promotions/{promotionId}")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetPromotion(int connectionId, string promotionId, CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetPromotions, ct);
        var promo = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetPromotionAsync(connection, promotionId, ct));
        if (promo == null)
            throw ApiException.NotFound("Promotion not found", code: "promotion_not_found");
        return Ok(promo);
    }

    [HttpPost("/marketplace/connections/{connectionId:int}/promotions/{promotionId}/products")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> UpdatePromotionProducts(
        int connectionId,
        string promotionId,
        [FromBody] PromotionProductsUpdate? body,
        CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.UpdatePromotions, ct);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.UpdatePromotionProductsAsync(connection, promotionId, body ?? new PromotionProductsUpdate(), ct));
        if (!result.Success)
            throw ApiException.BadRequest(result.ErrorMessage ?? "Promotion update failed", code: "promotion_update_failed");
        return Ok(result);
    }

    private async Task<int> RequireStoreAsync()
    {
        var selectedStoreId = await _currentUser.GetSelectedStoreIdAsync();
        if (selectedStoreId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");
        return selectedStoreId.Value;
    }
}
