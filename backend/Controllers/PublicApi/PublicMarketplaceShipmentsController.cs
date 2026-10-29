using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>Supplies, shipping units, and QR labels.</summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/connections/{connectionId:int}")]
[Tags("Shipments")]
public class PublicMarketplaceShipmentsController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly MarketplaceCommerceService _commerce;

    public PublicMarketplaceShipmentsController(PublicApiStoreContext storeCtx, MarketplaceCommerceService commerce)
    {
        _storeCtx = storeCtx;
        _commerce = commerce;
    }

    /// <summary>Lists shipments.</summary>
    /// <remarks>Live supplies. Query <c>limit</c> (max 200), <c>cursor</c>, <c>openOnly</c>. Not stored. Failure → <c>400 shipment_failed</c>.</remarks>
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

    /// <summary>Creates an open shipment.</summary>
    /// <remarks>Wildberries supply. Body: <c>name</c>, optional <c>destinationLocationId</c> (display only when the marketplace has no write). Failure → <c>400 shipment_failed</c>.</remarks>
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
            throw ApiException.BadRequest(code: "shipment_failed");
        return Ok(result);
    }

    /// <summary>Gets one shipment.</summary>
    /// <remarks>Includes order ids and boxes when the marketplace returns them. Missing → <c>404 shipment_not_found</c>.</remarks>
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
            throw ApiException.NotFound(code: "shipment_not_found");
        return Ok(shipment);
    }

    /// <summary>Adds orders to a shipment.</summary>
    /// <remarks>Body: <c>orderIds</c>. Failure → <c>400 shipment_failed</c>.</remarks>
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
            throw ApiException.BadRequest(code: "shipment_failed");
        return Ok(result);
    }

    /// <summary>Lists shipping units.</summary>
    /// <remarks>Wildberries TRBX boxes on the shipment.</remarks>
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

    /// <summary>Creates shipping units.</summary>
    /// <remarks>Body: <c>amount</c> (default 1). Wildberries pickup-point supplies only. Failure → <c>400 shipment_failed</c>.</remarks>
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
            throw ApiException.BadRequest(code: "shipment_failed");
        return Ok(result);
    }

    /// <summary>Puts orders into a shipping unit.</summary>
    /// <remarks>Body: <c>orderIds</c>. Failure → <c>400 shipment_failed</c>.</remarks>
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
            throw ApiException.BadRequest(code: "shipment_failed");
        return Ok(result);
    }

    /// <summary>Downloads a shipping-unit QR.</summary>
    /// <remarks>Same body as an order label. Empty box or marketplace error → <c>400 shipment_failed</c>.</remarks>
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
            throw ApiException.BadRequest(code: "shipment_failed");
        return Ok(result.Label);
    }

    /// <summary>Downloads a shipment QR.</summary>
    /// <remarks>Wildberries: only after the supply is handed over (<c>deliver</c>). Failure → <c>400 shipment_failed</c>.</remarks>
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
            throw ApiException.BadRequest(code: "shipment_failed");
        return Ok(result.Label);
    }
}
