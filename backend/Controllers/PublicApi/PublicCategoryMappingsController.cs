using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Public API v1 — category mappings on a connection, and which mapping each product uses.
/// </summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/connections/{connectionId:int}")]
[Tags("Mappings")]
public class PublicCategoryMappingsController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly ProductMappingSelectionService _selection;

    public PublicCategoryMappingsController(
        PublicApiStoreContext storeCtx,
        ProductMappingSelectionService selection)
    {
        _storeCtx = storeCtx;
        _selection = selection;
    }

    /// <summary>Lists category mappings.</summary>
    /// <remarks>One local category may have several rows when it exports to different marketplace categories.</remarks>
    [HttpGet("mappings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMappings(int storeId, int connectionId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var items = await _selection.ListAsync(storeId, connectionId, ct);
        return Ok(new
        {
            items = items.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                localCategoryId = m.LocalCategoryId,
                localCategoryName = m.LocalCategoryName,
                externalCategoryId = m.ExternalCategoryId,
                externalCategoryName = m.ExternalCategoryName,
            }),
            total = items.Count,
        });
    }

    /// <summary>Shows which mapping each product uses.</summary>
    /// <remarks>
    /// Body: <c>productIds</c>.
    /// Each product includes <c>selectedMappingId</c>, <c>needsSelection</c>, and <c>availableMappings</c> for its local category.
    /// </remarks>
    [HttpPost("product-mappings/resolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveProductMappings(
        int storeId,
        int connectionId,
        [FromBody] PublicResolveProductMappingsInput? body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var products = await _selection.ResolveAsync(
            storeId, connectionId, body?.productIds ?? [], ct);
        return Ok(new
        {
            products = products.Select(product => new
            {
                productId = product.ProductId,
                productName = product.ProductName,
                categoryId = product.CategoryId,
                categoryName = product.CategoryName,
                selectedMappingId = product.SelectedMappingId,
                needsSelection = product.NeedsSelection,
                availableMappings = product.AvailableMappings.Select(m => new
                {
                    id = m.Id,
                    name = m.Name,
                    externalCategoryId = m.ExternalCategoryId,
                    externalCategoryName = m.ExternalCategoryName,
                    localCategoryName = m.LocalCategoryName,
                }),
            }),
        });
    }

    /// <summary>Assigns a product to one mapping.</summary>
    /// <remarks>
    /// Does not change the field canvas.
    /// Body: <c>selections</c> [{ <c>productId</c>, <c>categoryMappingId</c> }].
    /// <c>categoryMappingId</c> must belong to this connection and to the product’s local category.
    /// A different local category → <c>400 mapping_category_mismatch</c>.
    /// Unknown mapping → <c>404 mapping_not_found</c>.
    /// No local category → <c>400 product_has_no_category</c>.
    /// </remarks>
    [HttpPost("product-mappings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveProductMappings(
        int storeId,
        int connectionId,
        [FromBody] PublicSaveProductMappingsInput? body,
        CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        var selections = (body?.selections ?? [])
            .Select(s => new MappingSelection(s.productId, s.categoryMappingId))
            .ToList();
        var updated = await _selection.SaveAsync(storeId, connectionId, selections, ct);
        return Ok(new { updated });
    }
}

public sealed class PublicResolveProductMappingsInput
{
    public List<int>? productIds { get; set; }
}

public sealed class PublicSaveProductMappingsInput
{
    public List<PublicProductMappingSelection>? selections { get; set; }
}

public sealed class PublicProductMappingSelection
{
    public int productId { get; set; }
    public int categoryMappingId { get; set; }
}
