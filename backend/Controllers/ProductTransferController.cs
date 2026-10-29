using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services;

namespace Delegat.Controllers;

/// <summary>
/// Endpoints for cloning or moving a product (with all related data) into another store.
/// Transfers are async - the request enqueues a job, ProductTransferBackgroundService
/// picks it up. Progress is surfaced through the operations side panel via SignalR.
/// </summary>
[ApiController]
[JwtAuthorize]
public class ProductTransferController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ProductTransferService _transferService;
    private readonly DataContext _context;
    private readonly IOperationLogger _operationLogger;
    private readonly QuotaService _quotaService;

    public ProductTransferController(
        ICurrentUserService currentUserService,
        ProductTransferService transferService,
        DataContext context,
        IOperationLogger operationLogger,
        QuotaService quotaService)
    {
        _currentUserService = currentUserService;
        _transferService = transferService;
        _context = context;
        _operationLogger = operationLogger;
        _quotaService = quotaService;
    }

    /// <summary>
    /// Read-only preview: what will happen if the product is cloned/moved into a target store.
    /// Surfaces SKU collisions, category match status, store-attribute mapping, marketplace
    /// links that will be broken, and the list of available groups in the target store.
    /// </summary>
    [HttpGet]
    [Route("/products/{productId}/transfer-preview")]
    public async Task<IActionResult> Preview(int productId, [FromQuery] int targetStoreId, CancellationToken ct)
    {
        var sourceStoreId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (sourceStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var userId = _currentUserService.GetUserId();
        if (userId == null)
            throw ApiException.Unauthorized();

        try
        {
            var preview = await _transferService.BuildPreviewAsync(
                productId, sourceStoreId.Value, targetStoreId, userId.Value, ct);
            return Ok(preview);
        }
        catch (InvalidOperationException)
        {
            throw ApiException.BadRequest(code: "product_transfer_invalid");
        }
    }

    /// <summary>
    /// Enqueue a clone or move operation. Returns immediately with an operationId -
    /// progress is reported through the OperationHub (side panel updates live).
    /// Background service ProductTransferBackgroundService picks up the job within ~1.5s.
    /// </summary>
    [HttpPost]
    [Route("/products/{productId}/transfer")]
    [RequiresPermission(Permissions.CatalogManage, requireStoreMembership: true)]
    public async Task<IActionResult> Transfer(int productId, [FromBody] TransferRequest request, CancellationToken ct)
    {
        var sourceStoreId = await _currentUserService.GetSelectedStoreIdAsync(ct);
        if (sourceStoreId == null)
            throw ApiException.BadRequest(code: "store_not_selected");

        var userId = _currentUserService.GetUserId();
        if (userId == null)
            throw ApiException.Unauthorized();

        // ---- Up-front validation (fail fast at submit time, not in the background) ----
        // We replicate the cheap checks from BuildPreviewAsync so the user gets immediate
        // feedback for obvious mistakes (no access, no source product, sku collision,
        // missing confirmation). Heavy work (file copy) is left to the worker.

        request.sourceProductId = productId;

        if (request.mode != "clone" && request.mode != "move")
            throw ApiException.BadRequest(code: "transfer_mode_invalid");
        if (request.targetStoreId == sourceStoreId.Value && request.mode == "move")
            throw ApiException.BadRequest(code: "same_store_transfer");
        if (request.mode == "move" && await _context.stores.CountAsync(ct) <= 1)
            throw ApiException.BadRequest(code: "single_store_move_forbidden");

        var targetStore = await _context.stores
            .Include(s => s.members)
            .FirstOrDefaultAsync(s => s.Id == request.targetStoreId, ct);
        if (targetStore == null)
            throw ApiException.BadRequest(code: "target_store_not_found");
        if (!targetStore.members.Any(m => m.Id == userId.Value))
            throw ApiException.Forbidden(code: "access_denied_store");

        var source = await _context.products
            .Include(p => p.marketplaceLinks)
            .Include(p => p.files)
            .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == sourceStoreId.Value, ct);
        if (source == null)
            throw ApiException.NotFound(code: "product_not_found");

        if (source.marketplaceLinks.Any() && !request.confirmBreakLinks)
            throw ApiException.BadRequest(code: "transfer_links_confirmation_required");

        var newSku = !string.IsNullOrWhiteSpace(request.newSku)
            ? request.newSku!.Trim()
            : source.sku;
        var skuTaken = await _context.products
            .AnyAsync(p => p.storeId == request.targetStoreId && p.sku == newSku, ct);
        if (skuTaken)
            throw new ApiException(
                statusCode: StatusCodes.Status400BadRequest,
                data: new { sku = newSku },
                code: "sku_already_used");

        // Target store quotas (clone and move both create a new product; files only when copying).
        var shouldCopyFiles = request.mode == "move" || request.copyFiles;
        var fileCount = shouldCopyFiles ? source.files.Count : 0;
        var fileBytes = shouldCopyFiles ? source.files.Sum(f => f.length) : 0;
        await _quotaService.EnsureTransferQuotasAsync(request.targetStoreId, fileCount, fileBytes);

        // ---- Enqueue ----
        var operationId = Guid.NewGuid().ToString("N");
        // Total = all copyable entities (product + attrs + store attrs + folders + files + images + notes + variants)
        var totalItems = 1 + fileCount; // minimal: product itself + files (full count computed by worker)

        var pending = new PendingProductTransfer
        {
            sourceProductId = productId,
            sourceStoreId = sourceStoreId.Value,
            targetStoreId = request.targetStoreId,
            mode = request.mode,
            newSku = request.newSku,
            categoryAction = request.categoryAction,
            categoryId = request.categoryId,
            groupAction = request.groupAction,
            groupId = request.groupId,
            groupName = request.groupName,
            confirmBreakLinks = request.confirmBreakLinks,
            syncMissingCategoryAttributes = request.syncMissingCategoryAttributes,
            copyFiles = shouldCopyFiles,
            authorUserId = userId.Value,
            batchId = operationId,
            status = ProductTransferStatus.Pending,
            createdAt = DateTime.UtcNow
        };
        _context.pendingProductTransfers.Add(pending);
        await _context.SaveChangesAsync(ct);

        // Start the operation entry up-front so the side panel sees it before the worker picks it up.
        // Logging against the *source* store - that's where the user is sitting when they hit
        // submit, and SignalR broadcasts go to `store-{currentStore}`. Logging against the
        // target store would silently route updates away from the user's current view.
        // The new product still lands in the target store; only operation visibility is anchored
        // to where the action was initiated.
        var title = request.mode == "move"
            ? $"Moving product \"{source.name}\" -> {targetStore.name}"
            : $"Cloning product \"{source.name}\" -> {targetStore.name}";
        await _operationLogger.StartAsync(operationId, sourceStoreId.Value, userId.Value,
            "product-transfer", title, totalItems: totalItems,
            batchId: operationId, batchType: "product-transfer");

        return Ok(new
        {
            queued = true,
            operationId,
            totalItems
        });
    }
}
