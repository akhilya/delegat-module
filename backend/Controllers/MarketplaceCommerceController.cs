using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

/// <summary>
/// JWT cabinet API for live marketplace prices, orders, labels, and promotions.
/// Orders have cabinet SPA mailboxes; nothing is stored. No promotion screens.
/// </summary>
[ApiController]
[JwtAuthorize]
public class MarketplaceCommerceController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly MarketplaceCommerceService _commerce;

    public MarketplaceCommerceController(
        ICurrentUserService currentUser,
        MarketplaceCommerceService commerce)
    {
        _currentUser = currentUser;
        _commerce = commerce;
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
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
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

        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrders, ct);
        var query = MarketplaceCommerceService.BuildOrderQuery(
            page, pageSize, cursor, status, from, to, channel, search);
        var result = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetOrdersAsync(connection, query, ct));

        var items = result.Items ?? [];
        // Adapters may already set imageUrl (WB CDN, Ozon product/info). Local files fill the rest.
        await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, items, ct);
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

    [HttpGet("/marketplace/connections/{connectionId:int}/orders/{orderId}")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetOrder(int connectionId, string orderId, CancellationToken ct)
    {
        var storeId = await RequireStoreAsync();
        var (connection, adapter) = await _commerce.RequireAsync(
            storeId, connectionId, MarketplaceCapabilities.GetOrders, ct);
        var order = await MarketplaceCommerceService.InvokeAsync(
            () => adapter.GetOrderAsync(connection, orderId, ct));
        if (order == null)
            throw ApiException.NotFound("Order not found", code: "order_not_found");
        await _commerce.AttachLocalOrderItemImagesAsync(connection.Id, [order], ct);
        return Ok(order);
    }

    [HttpPost("/marketplace/connections/{connectionId:int}/orders/{orderId}/status")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
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
        return Ok(result);
    }

    [HttpGet("/marketplace/connections/{connectionId:int}/orders/{orderId}/label")]
    [RequiresPermission(Permissions.MarketplaceManage, requireStoreMembership: true)]
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
