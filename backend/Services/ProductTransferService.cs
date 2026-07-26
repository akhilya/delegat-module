using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;

namespace Delegat.Services;

/// <summary>
/// Clone or move a product (with all related data) into another store.
///
/// Move semantics: Move = atomic Clone + soft-delete original. The original
/// product enters its source-store trash (recoverable via /restore), so
/// no permanent data loss can occur even if downstream steps fail.
///
/// What travels with the product:
///   - Product fields (sku, name, description, barcode, price, qty)
///   - Category (matched by name, optionally auto-cloned with its attributes)
///   - Category attribute values (re-mapped by attribute key/name)
///   - Store attribute values (re-mapped by store attribute key)
///   - Files (physical MinIO copy + DB rows)
///   - ProductFolder tree (whole hierarchy preserved)
///   - ImageReferences (rebuilt against new file IDs)
///   - Notes
///   - ProductVariants (variant marketplace links are dropped)
///
/// What does NOT travel:
///   - ProductMarketplaceLink (store-scoped connections; broken by design)
///   - ProductGroupMember (groups are store-scoped; user picks a target group)
/// </summary>
public class ProductTransferService
{
    private readonly DataContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStorageService _storageService;
    private readonly ILogger<ProductTransferService> _logger;

    public ProductTransferService(
        DataContext context,
        IUnitOfWork unitOfWork,
        IStorageService storageService,
        ILogger<ProductTransferService> logger)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _storageService = storageService;
        _logger = logger;
    }

    #region Preview

    /// <summary>
    /// Compute the read-only preview shown in the dialog before the user confirms.
    /// </summary>
    public async Task<TransferPreview> BuildPreviewAsync(
        int sourceProductId,
        int sourceStoreId,
        int targetStoreId,
        int userId,
        CancellationToken ct = default)
    {
        // Same-store target is allowed (clone-in-place is a useful operation -
        // SKU collision will force the user to pick a new SKU). Move-into-self
        // is blocked at execute-time, not preview-time, so the user can still
        // see the preview before deciding which mode to use.

        var targetStore = await _context.stores
            .Include(s => s.members)
            .FirstOrDefaultAsync(s => s.Id == targetStoreId, ct);

        if (targetStore == null)
            throw new InvalidOperationException($"Target store #{targetStoreId} not found");

        if (!targetStore.members.Any(m => m.Id == userId))
            throw new InvalidOperationException("No access to the target store");

        var product = await _context.products
            .AsSplitQuery()
            .Include(p => p.category)
                .ThenInclude(c => c!.attributes)
            .Include(p => p.attributeValues)
                .ThenInclude(av => av.attribute)
            .Include(p => p.storeAttributeValues)
                .ThenInclude(sav => sav.storeAttribute)
            .Include(p => p.files)
            .Include(p => p.images)
            .Include(p => p.folders)
            .Include(p => p.notes)
            .Include(p => p.variants)
            .Include(p => p.marketplaceLinks)
                .ThenInclude(l => l.connection)
            .FirstOrDefaultAsync(p => p.Id == sourceProductId && p.storeId == sourceStoreId, ct);

        if (product == null)
            throw new InvalidOperationException("Source product not found");

        // SKU collision in target store (excluding soft-deleted to mirror UI behavior)
        var skuTaken = await _context.products
            .AnyAsync(p => p.storeId == targetStoreId && p.sku == product.sku, ct);

        // Category match by name (case-insensitive)
        Category? matchedCategory = null;
        var unmappableAttrNames = new List<string>();
        var mappableAttrCount = 0;

        if (product.category != null)
        {
            matchedCategory = await _context.categories
                .Include(c => c.attributes)
                .FirstOrDefaultAsync(c =>
                    c.storeId == targetStoreId &&
                    c.name == product.category.name, ct);

            if (matchedCategory != null && product.category!.attributes.Any())
            {
                // Compare source category ATTRIBUTE DEFINITIONS (not just product values)
                // against target category. This surfaces ALL missing definitions - even those
                // the product doesn't currently have a value for - so the user can choose to
                // sync them before cloning. Otherwise the cloned product can't be edited with
                // the full set of fields.
                var targetKeys = new HashSet<string>(matchedCategory.attributes.Select(a => a.key));
                foreach (var srcAttr in product.category!.attributes)
                {
                    if (targetKeys.Contains(srcAttr.key))
                        mappableAttrCount++;
                    else
                        unmappableAttrNames.Add(srcAttr.name ?? $"#{srcAttr.Id}");
                }
            }
            else if (matchedCategory == null && product.category!.attributes.Any())
            {
                // No category match - all attribute definitions become unmappable unless we auto-create.
                foreach (var srcAttr in product.category!.attributes)
                    unmappableAttrNames.Add(srcAttr.name ?? $"#{srcAttr.Id}");
            }
        }

        // Store-attribute mapping report
        var targetStoreAttrs = await _context.storeAttributes
            .Where(sa => sa.storeId == targetStoreId)
            .ToListAsync(ct);
        var targetStoreAttrKeys = new HashSet<string>(targetStoreAttrs.Select(a => a.key));

        var unmappableStoreAttrs = new List<string>();
        var mappableStoreAttrCount = 0;
        foreach (var sav in product.storeAttributeValues)
        {
            var srcKey = sav.storeAttribute?.key;
            if (srcKey != null && targetStoreAttrKeys.Contains(srcKey))
                mappableStoreAttrCount++;
            else
                unmappableStoreAttrs.Add(sav.storeAttribute?.name ?? $"#{sav.storeAttributeId}");
        }

        // Marketplace links to break (informational)
        var linksToBreak = product.marketplaceLinks
            .Select(l => new BrokenLinkPreview
            {
                connectionId = l.connectionId,
                connectionName = l.connection?.name ?? l.connection?.adapterKey ?? "-",
                externalProductId = l.externalProductId,
                status = l.status.ToString()
            })
            .ToList();

        // Target store groups (for the "place into group" dropdown)
        var targetGroups = await _context.Set<ProductGroup>()
            .Where(g => g.storeId == targetStoreId)
            .OrderBy(g => g.name)
            .Select(g => new GroupOption { id = g.Id, name = g.name })
            .ToListAsync(ct);

        return new TransferPreview
        {
            sourceProduct = new SourceProductInfo
            {
                id = product.Id,
                sku = product.sku,
                name = product.name,
                categoryName = product.category?.name
            },
            targetStore = new TargetStoreInfo
            {
                id = targetStore.Id,
                name = targetStore.name
            },
            skuConflict = skuTaken,
            matchedCategory = matchedCategory != null
                ? new MatchedCategoryInfo
                {
                    id = matchedCategory.Id,
                    name = matchedCategory.name,
                    attributesMappable = mappableAttrCount,
                    attributesUnmappable = unmappableAttrNames
                }
                : null,
            storeAttributesMappable = mappableStoreAttrCount,
            storeAttributesUnmappable = unmappableStoreAttrs,
            marketplaceLinksToBreak = linksToBreak,
            targetGroups = targetGroups,
            filesCount = product.files.Count,
            foldersCount = product.folders.Count,
            imagesCount = product.images.Count,
            notesCount = product.notes.Count,
            variantsCount = product.variants.Count
        };
    }

    #endregion

    #region Transfer

    /// <summary>
    /// Execute clone or move. Throws on validation failure; commits atomically on success.
    ///
    /// When <paramref name="operationLogger"/> is provided, per-file progress and warnings
    /// are emitted to that operation (so the side panel and SignalR clients see them).
    /// Without a logger, the method runs silently - useful for quick test paths.
    ///
    /// File-copy failures are NOT fatal: failed files are skipped, logged as warnings,
    /// and the resulting product is still created (with a "partial" final status if any
    /// file failed). User cancellation via <paramref name="ct"/> rolls everything back -
    /// no new product, all copied blobs cleaned up best-effort.
    /// </summary>
    public async Task<TransferResult> ExecuteAsync(
        TransferRequest request,
        int sourceStoreId,
        int userId,
        CancellationToken ct = default,
        IOperationLogger? operationLogger = null,
        string? operationId = null)
    {
        // ---- Validate ----------------------------------------------------------------

        if (request.mode != "clone" && request.mode != "move")
            throw new InvalidOperationException($"Unknown mode: {request.mode}");

        // Same-store is OK for clone (just an in-store duplicate), but move-into-self
        // is meaningless - it would soft-delete the original and create the same thing
        // back in the same store.
        if (request.targetStoreId == sourceStoreId && request.mode == "move")
            throw new InvalidOperationException("Cannot move into the same store. Use clone mode instead.");

        var targetStore = await _context.stores
            .Include(s => s.members)
            .FirstOrDefaultAsync(s => s.Id == request.targetStoreId, ct);

        if (targetStore == null)
            throw new InvalidOperationException($"Target store #{request.targetStoreId} not found");

        if (!targetStore.members.Any(m => m.Id == userId))
            throw new InvalidOperationException("No access to the target store");

        var source = await _context.products
            .AsSplitQuery()
            .Include(p => p.category)
                .ThenInclude(c => c!.attributes)
            .Include(p => p.attributeValues)
                .ThenInclude(av => av.attribute)
            .Include(p => p.storeAttributeValues)
                .ThenInclude(sav => sav.storeAttribute)
            .Include(p => p.files)
                .ThenInclude(f => f.tags)
                    .ThenInclude(t => t.tagDefinition)
            .Include(p => p.images)
            .Include(p => p.folders)
            .Include(p => p.notes)
            .Include(p => p.variants)
            .Include(p => p.marketplaceLinks)
            .FirstOrDefaultAsync(p => p.Id == request.sourceProductId && p.storeId == sourceStoreId, ct);

        if (source == null)
            throw new InvalidOperationException("Source product not found");

        // Require explicit user confirmation before breaking active marketplace links.
        if (source.marketplaceLinks.Any() && !request.confirmBreakLinks)
            throw new InvalidOperationException(
                "The product has marketplace links. Confirm breaking them (confirmBreakLinks=true)");

        // SKU resolution (override or original) + collision check
        var newSku = !string.IsNullOrWhiteSpace(request.newSku)
            ? request.newSku!.Trim()
            : source.sku;

        var skuTaken = await _context.products
            .AnyAsync(p => p.storeId == request.targetStoreId && p.sku == newSku, ct);

        if (skuTaken)
            throw new InvalidOperationException(
                $"SKU '{newSku}' is already taken in the target store. Provide a different one via newSku.");

        // ---- Resolve target category --------------------------------------------------

        Category? targetCategory = null;
        Dictionary<int, int> attributeIdMap = new(); // sourceAttrId -> targetAttrId

        if (source.categoryId.HasValue)
        {
            switch (request.categoryAction)
            {
                case "match":
                    // Re-use CloneCategoryAsync: if category exists it returns it
                    // (and syncs missing attributes when the flag is set);
                    // if it doesn't exist, it creates one. Same logic as "create".
                    if (source.category != null)
                    {
                        targetCategory = await CloneCategoryAsync(
                            source.category, request.targetStoreId,
                            request.syncMissingCategoryAttributes, ct);
                    }
                    break;

                case "use":
                    if (!request.categoryId.HasValue)
                        throw new InvalidOperationException("categoryAction=use requires categoryId");
                    targetCategory = await _context.categories
                        .Include(c => c.attributes)
                        .FirstOrDefaultAsync(c =>
                            c.Id == request.categoryId.Value &&
                            c.storeId == request.targetStoreId, ct);
                    if (targetCategory == null)
                        throw new InvalidOperationException("The specified category was not found in the target store");
                    break;

                case "create":
                    if (source.category == null)
                        throw new InvalidOperationException("categoryAction=create requires a source category");
                    targetCategory = await CloneCategoryAsync(
                        source.category, request.targetStoreId,
                        request.syncMissingCategoryAttributes, ct);
                    break;

                case "none":
                default:
                    targetCategory = null;
                    break;
            }
        }

        // Build attribute id map (source CategoryAttribute.Id -> target CategoryAttribute.Id)
        if (targetCategory != null && source.category != null)
        {
            var targetByKey = targetCategory.attributes.ToDictionary(a => a.key, a => a.Id);
            foreach (var srcAttr in source.category.attributes)
            {
                if (targetByKey.TryGetValue(srcAttr.key, out var targetId))
                    attributeIdMap[srcAttr.Id] = targetId;
            }
        }

        if (targetCategory != null)
        {
            var mapped = attributeIdMap.Count;
            var total = source.category?.attributes?.Count ?? 0;
            await Log("info",
                $"Category: \"{targetCategory.name}\" (ID {targetCategory.Id}), attributes mapped: {mapped}/{total}");
        }
        else if (source.categoryId.HasValue)
        {
            await Log("warning", "Category: not assigned in the target store");
        }

        // ---- Resolve store attribute id map ------------------------------------------

        var targetStoreAttrs = await _context.storeAttributes
            .Where(sa => sa.storeId == request.targetStoreId)
            .ToListAsync(ct);
        var targetStoreAttrByKey = targetStoreAttrs.ToDictionary(a => a.key, a => a.Id);

        var storeAttrIdMap = new Dictionary<int, int>();
        foreach (var srcSav in source.storeAttributeValues)
        {
            var key = srcSav.storeAttribute?.key;
            if (key != null && targetStoreAttrByKey.TryGetValue(key, out var targetId))
                storeAttrIdMap[srcSav.storeAttributeId] = targetId;
        }

        // ---- Resolve target group ----------------------------------------------------

        ProductGroup? targetGroup = null;

        switch (request.groupAction)
        {
            case "use":
                if (!request.groupId.HasValue)
                    throw new InvalidOperationException("groupAction=use requires groupId");
                targetGroup = await _context.Set<ProductGroup>()
                    .FirstOrDefaultAsync(g =>
                        g.Id == request.groupId.Value &&
                        g.storeId == request.targetStoreId, ct);
                if (targetGroup == null)
                    throw new InvalidOperationException("The specified group was not found in the target store");
                break;

            case "create":
                if (string.IsNullOrWhiteSpace(request.groupName))
                    throw new InvalidOperationException("groupAction=create requires groupName");
                targetGroup = new ProductGroup();
                targetGroup.AddFill(new ProductGroup.AddForm
                {
                    name = request.groupName!.Trim(),
                    description = $"Created when transferring a product from store #{sourceStoreId}",
                    storeId = request.targetStoreId
                });
                _context.Set<ProductGroup>().Add(targetGroup);
                // Save here so we have an Id for the membership row created later in TX.
                // This still rolls back if the outer transaction fails.
                break;

            case "none":
            default:
                break;
        }

        // ---- Plan MinIO copies -------------------------------------------------------
        // We pre-plan source->dest paths so we can roll them back if the DB transaction
        // fails. Path includes the new productId, which we don't have yet - placeholder
        // it, fix after the new product is saved.

        var newProductGuid = Guid.NewGuid().ToString("N").Substring(0, 8);

        var transferred = new List<(int newFileId, string destPath)>();
        var copiedMinIOObjects = new List<string>(); // for cleanup on failure

        TransferResult result = null!;

        // Helper: log a step to the operation panel (no-op when running without logger)
        async Task Log(string level, string message)
        {
            if (operationLogger != null && operationId != null)
                await operationLogger.LogAsync(operationId, sourceStoreId, level, message);
        }

        try
        {
            // Total items = all copyable entities (for unified progress bar)
            var totalItems = source.attributeValues.Count
                + source.storeAttributeValues.Count
                + source.folders.Count
                + source.files.Count
                + source.images.Count
                + source.notes.Count
                + source.variants.Count
                + 1; // +1 for the product itself
            var completedItems = 0;
            var failedItems = 0;

            async Task Progress()
            {
                if (operationLogger != null && operationId != null)
                    await operationLogger.ProgressAsync(operationId, sourceStoreId,
                        totalItems, completedItems, failedItems);
            }

            await Progress();

            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                // ---- Create new Product --------------------------------------------------

                await Log("info", $"Creating product ({newSku})");

                Product newProduct;
                try
                {
                    newProduct = new Product
                    {
                        sku = newSku,
                        barcode = source.barcode,
                        name = source.name,
                        description = source.description,
                        storageQuantity = source.storageQuantity,
                        price = source.price,
                        storeId = request.targetStoreId,
                        categoryId = targetCategory?.Id,
                        createdAt = DateTime.UtcNow,
                        updatedAt = DateTime.UtcNow
                    };
                    _context.products.Add(newProduct);
                    await _context.SaveChangesAsync(ct);

                    if (targetGroup != null && targetGroup.Id == 0)
                        await _context.SaveChangesAsync(ct);

                    completedItems++;
                    await Progress();
                    await Log("success", $"Product created (ID: {newProduct.Id})");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await Log("error", $"Error creating product: {ex.Message}");
                    throw;
                }

                // ---- Copy ProductAttributeValues (mapped) --------------------------------

                var copiedAttrValues = 0;
                var skippedAttrValues = 0;
                try
                {
                    if (targetCategory != null)
                    {
                        foreach (var srcAv in source.attributeValues)
                        {
                            if (!attributeIdMap.TryGetValue(srcAv.attributeId, out var newAttrId))
                            {
                                skippedAttrValues++;
                                continue;
                            }
                            _context.productAttributeValues.Add(new ProductAttributeValue
                            {
                                productId = newProduct.Id,
                                attributeId = newAttrId,
                                value = srcAv.value
                            });
                            copiedAttrValues++;
                        }
                    }

                    if (copiedAttrValues > 0 || skippedAttrValues > 0)
                    {
                        completedItems += copiedAttrValues;
                        failedItems += skippedAttrValues;
                        await Progress();
                        await Log("info",
                            $"Category attributes: {copiedAttrValues} copied" +
                            (skippedAttrValues > 0 ? $", {skippedAttrValues} skipped (not in target category)" : ""));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await Log("error", $"Error copying category attributes: {ex.Message}");
                    throw;
                }

                // ---- Copy StoreProductAttributeValues (mapped) ---------------------------

                var copiedStoreAttrs = 0;
                var skippedStoreAttrs = 0;
                try
                {
                    foreach (var srcSav in source.storeAttributeValues)
                    {
                        if (!storeAttrIdMap.TryGetValue(srcSav.storeAttributeId, out var newAttrId))
                        {
                            skippedStoreAttrs++;
                            continue;
                        }
                        _context.storeProductAttributeValues.Add(new StoreProductAttributeValue
                        {
                            productId = newProduct.Id,
                            storeAttributeId = newAttrId,
                            value = srcSav.value
                        });
                        copiedStoreAttrs++;
                    }

                    if (copiedStoreAttrs > 0 || skippedStoreAttrs > 0)
                    {
                        completedItems += copiedStoreAttrs;
                        failedItems += skippedStoreAttrs;
                        await Progress();
                        await Log("info",
                            $"Store attributes: {copiedStoreAttrs} copied" +
                            (skippedStoreAttrs > 0 ? $", {skippedStoreAttrs} skipped" : ""));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await Log("error", $"Error copying store attributes: {ex.Message}");
                    throw;
                }

                // ---- Copy ProductFolder tree ---------------------------------------------

                var folderIdMap = new Dictionary<int, int>();
                var pendingFolders = source.folders.ToList();
                try
                {
                    if (pendingFolders.Count > 0)
                        await Log("info", $"Copying folders: {pendingFolders.Count}");

                    while (pendingFolders.Count > 0)
                    {
                        var progressed = false;
                        foreach (var f in pendingFolders.ToList())
                        {
                            if (f.parentFolderId.HasValue && !folderIdMap.ContainsKey(f.parentFolderId.Value))
                                continue;

                            var newFolder = new ProductFolder
                            {
                                name = f.name,
                                productId = newProduct.Id,
                                parentFolderId = f.parentFolderId.HasValue
                                    ? folderIdMap[f.parentFolderId.Value]
                                    : null,
                                createdAt = f.createdAt
                            };
                            _context.productFolders.Add(newFolder);
                            await _context.SaveChangesAsync(ct);
                            folderIdMap[f.Id] = newFolder.Id;
                            pendingFolders.Remove(f);
                            progressed = true;
                            completedItems++;
                        }
                        if (!progressed)
                        {
                            await Log("warning", $"Failed to copy {pendingFolders.Count} folders (hierarchy broken)");
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await Log("error", $"Error copying folders: {ex.Message}");
                    throw;
                }

                // ---- Copy Files (MinIO + DB) ---------------------------------------------

                var fileIdMap = new Dictionary<int, int>();
                var totalFiles = source.files.Count;
                var copiedFileCount = 0;
                var failedFileCount = 0;

                if (totalFiles > 0)
                    await Log("info", $"Copying files: {totalFiles}");

                foreach (var srcFile in source.files)
                {
                    ct.ThrowIfCancellationRequested();

                    var fileName = System.IO.Path.GetFileName(srcFile.filePath);
                    var displayName = srcFile.displayName ?? fileName;
                    var uniqueKey = Guid.NewGuid().ToString("N");
                    var newPath = $"{request.targetStoreId}/{newProduct.Id}/{uniqueKey}/{fileName}";

                    bool copied;
                    try
                    {
                        copied = await _storageService.CopyAsync(srcFile.filePath, newPath, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Transfer: copy failed for {Path}", srcFile.filePath);
                        copied = false;
                    }

                    if (!copied)
                    {
                        failedFileCount++;
                        failedItems++;
                        await Progress();
                        await Log("warning", $"File not copied, skipped: {displayName}");
                        continue;
                    }

                    copiedMinIOObjects.Add(newPath);

                    try
                    {
                        var newFile = new Models.File
                        {
                            productId = newProduct.Id,
                            authorUserId = userId,
                            filePath = newPath,
                            displayName = srcFile.displayName,
                            uploadDateTime = DateTime.UtcNow,
                            fileType = srcFile.fileType,
                            contentType = srcFile.contentType,
                            length = srcFile.length,
                            folderId = srcFile.folderId.HasValue && folderIdMap.ContainsKey(srcFile.folderId.Value)
                                ? folderIdMap[srcFile.folderId.Value]
                                : null
                        };
                        _context.Set<Models.File>().Add(newFile);
                        await _context.SaveChangesAsync(ct);

                        fileIdMap[srcFile.Id] = newFile.Id;
                        transferred.Add((newFile.Id, newPath));

                        // Copy file tags - map TagDefinitions to target store
                        foreach (var srcTag in srcFile.tags)
                        {
                            var srcDef = srcTag.tagDefinition;
                            if (srcDef == null) continue;

                            int targetTagId;
                            if (srcDef.storeId == request.targetStoreId)
                            {
                                // Same store clone - reuse directly
                                targetTagId = srcTag.tagDefinitionId;
                            }
                            else
                            {
                                // Cross-store - find or create matching TagDefinition in target
                                var existing = await _context.tagDefinitions
                                    .FirstOrDefaultAsync(td =>
                                        td.storeId == request.targetStoreId && td.name == srcDef.name, ct);
                                if (existing != null)
                                {
                                    targetTagId = existing.Id;
                                }
                                else
                                {
                                    var newDef = new TagDefinition
                                    {
                                        name = srcDef.name,
                                        color = srcDef.color,
                                        storeId = request.targetStoreId,
                                        createdAt = DateTime.UtcNow
                                    };
                                    _context.tagDefinitions.Add(newDef);
                                    await _context.SaveChangesAsync(ct);
                                    targetTagId = newDef.Id;
                                }
                            }

                            _context.tags.Add(new FileTag
                            {
                                fileId = newFile.Id,
                                tagDefinitionId = targetTagId
                            });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failedFileCount++;
                        failedItems++;
                        await Progress();
                        await Log("warning", $"Error saving file {displayName}: {ex.Message}");
                        continue;
                    }

                    copiedFileCount++;
                    completedItems++;
                    await Progress();
                }

                if (totalFiles > 0)
                    await Log("info", $"Files: {copiedFileCount} copied" + (failedFileCount > 0 ? $", {failedFileCount} failed" : ""));

                // ---- Copy ImageReferences (rebuilt with new fileIds) ---------------------

                var imagesCopied = 0;
                try
                {
                    foreach (var srcImg in source.images.OrderBy(i => i.index))
                    {
                        if (!fileIdMap.TryGetValue(srcImg.fileId, out var newFileId))
                            continue;
                        _context.Set<ImageReference>().Add(new ImageReference
                        {
                            productId = newProduct.Id,
                            fileId = newFileId,
                            index = srcImg.index
                        });
                        imagesCopied++;
                        completedItems++;
                    }
                    if (imagesCopied > 0)
                    {
                        await Progress();
                        await Log("info", $"Images: {imagesCopied} linked");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await Log("error", $"Error linking images: {ex.Message}");
                    throw;
                }

                // ---- Copy Notes ----------------------------------------------------------

                try
                {
                    foreach (var srcNote in source.notes)
                    {
                        _context.notes.Add(new Note
                        {
                            productId = newProduct.Id,
                            text = srcNote.text,
                            importance = srcNote.importance
                        });
                        completedItems++;
                    }
                    if (source.notes.Count > 0)
                    {
                        await Progress();
                        await Log("info", $"Notes: {source.notes.Count} copied");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await Log("error", $"Error copying notes: {ex.Message}");
                    throw;
                }

                // ---- Copy Variants (without marketplace links) ---------------------------

                try
                {
                    foreach (var srcVar in source.variants)
                    {
                        _context.productVariants.Add(new ProductVariant
                        {
                            productId = newProduct.Id,
                            sku = srcVar.sku,
                            barcode = srcVar.barcode,
                            size = srcVar.size,
                            price = srcVar.price,
                            quantity = srcVar.quantity,
                            sortOrder = srcVar.sortOrder,
                            createdAt = DateTime.UtcNow,
                            updatedAt = DateTime.UtcNow
                        });
                        completedItems++;
                    }
                    if (source.variants.Count > 0)
                    {
                        await Progress();
                        await Log("info", $"Variants: {source.variants.Count} copied");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    await Log("error", $"Error copying variants: {ex.Message}");
                    throw;
                }

                // ---- Group membership (target store group) -------------------------------

                if (targetGroup != null)
                {
                    try
                    {
                        var maxOrder = await _context.productGroupMembers
                            .Where(m => m.groupId == targetGroup.Id)
                            .Select(m => (int?)m.sortOrder)
                            .MaxAsync(ct) ?? 0;

                        _context.productGroupMembers.Add(new ProductGroupMember
                        {
                            groupId = targetGroup.Id,
                            productId = newProduct.Id,
                            sortOrder = maxOrder + 1,
                            addedAt = DateTime.UtcNow
                        });
                        await Log("info", $"Added to group: {targetGroup.name}");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        await Log("error", $"Error adding to group: {ex.Message}");
                        throw;
                    }
                }

                // ---- Move-only: soft-delete original -------------------------------------

                if (request.mode == "move")
                {
                    source.deletedAt = DateTime.UtcNow;
                    _context.products.Update(source);
                    await Log("info", "Original moved to trash");
                }

                await Log("info", "Saving");
                await _context.SaveChangesAsync(ct);

                result = new TransferResult
                {
                    success = true,
                    mode = request.mode,
                    newProductId = newProduct.Id,
                    targetStoreId = request.targetStoreId,
                    filesCopied = copiedFileCount,
                    filesFailed = failedFileCount,
                    imagesCopied = source.images.Count(i => fileIdMap.ContainsKey(i.fileId)),
                    notesCopied = source.notes.Count,
                    variantsCopied = source.variants.Count,
                    attributeValuesCopied = attributeIdMap.Count > 0
                        ? source.attributeValues.Count(av => attributeIdMap.ContainsKey(av.attributeId))
                        : 0,
                    storeAttributeValuesCopied = source.storeAttributeValues
                        .Count(sav => storeAttrIdMap.ContainsKey(sav.storeAttributeId)),
                    marketplaceLinksBroken = source.marketplaceLinks.Count,
                    originalSoftDeleted = request.mode == "move"
                };
            });

            // (this assignment lives in closure above; result is captured by ref via outer var)
            return result!;
        }
        catch (Exception)
        {
            // Best-effort cleanup of MinIO objects we created - DB rollback already happened.
            foreach (var path in copiedMinIOObjects)
            {
                try { await _storageService.DeleteAsync(path, CancellationToken.None); }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx,
                        "Failed to clean up orphaned MinIO object after rollback: {Path}", path);
                }
            }
            throw;
        }
    }

    #endregion

    #region Private helpers

    /// <summary>
    /// Clone a category (with all its CategoryAttribute definitions) into the target store.
    /// </summary>
    private async Task<Category> CloneCategoryAsync(
        Category source, int targetStoreId, bool syncMissingAttributes, CancellationToken ct)
    {
        // Check if a category with the same name already exists in the target store.
        var existing = await _context.categories
            .Include(c => c.attributes)
            .FirstOrDefaultAsync(c => c.storeId == targetStoreId && c.name == source.name, ct);

        if (existing != null)
        {
            if (syncMissingAttributes)
            {
                // Category exists but may have fewer attributes than the source.
                // User explicitly opted in to sync missing definitions so the
                // attribute-value ID mapping can cover all source fields.
                var existingKeys = new HashSet<string>(existing.attributes.Select(a => a.key));
                var added = 0;

                foreach (var srcAttr in source.attributes)
                {
                    if (existingKeys.Contains(srcAttr.key))
                        continue;

                    _context.categoryAttributes.Add(new CategoryAttribute
                    {
                        categoryId = existing.Id,
                        name = srcAttr.name,
                        key = srcAttr.key,
                        type = srcAttr.type,
                        isRequired = srcAttr.isRequired,
                        isUnique = srcAttr.isUnique,
                        sortOrder = srcAttr.sortOrder,
                        options = srcAttr.options,
                        defaultValue = srcAttr.defaultValue,
                        description = srcAttr.description,
                        descriptionInTooltip = srcAttr.descriptionInTooltip,
                        group = srcAttr.group
                    });
                    added++;
                }

                if (added > 0)
                {
                    await _context.SaveChangesAsync(ct);
                    _logger.LogInformation(
                        "[Transfer] Category '{Name}' in store {StoreId}: synced {Added} missing attribute(s) by user request",
                        existing.name, targetStoreId, added);

                    // Re-load so the caller sees the full set of attributes.
                    return await _context.categories
                        .Include(c => c.attributes)
                        .FirstAsync(c => c.Id == existing.Id, ct);
                }
            }

            return existing;
        }

        var newCat = new Category
        {
            name = source.name,
            description = source.description,
            hasSizes = source.hasSizes,
            storeId = targetStoreId,
            createdAt = DateTime.UtcNow
        };
        _context.categories.Add(newCat);
        await _context.SaveChangesAsync(ct);

        foreach (var srcAttr in source.attributes)
        {
            _context.categoryAttributes.Add(new CategoryAttribute
            {
                categoryId = newCat.Id,
                name = srcAttr.name,
                key = srcAttr.key,
                type = srcAttr.type,
                isRequired = srcAttr.isRequired,
                isUnique = srcAttr.isUnique,
                sortOrder = srcAttr.sortOrder,
                options = srcAttr.options,
                defaultValue = srcAttr.defaultValue,
                description = srcAttr.description,
                descriptionInTooltip = srcAttr.descriptionInTooltip,
                group = srcAttr.group
            });
        }
        await _context.SaveChangesAsync(ct);

        // Re-load with attributes attached for the caller.
        return await _context.categories
            .Include(c => c.attributes)
            .FirstAsync(c => c.Id == newCat.Id, ct);
    }

    #endregion
}

#region DTOs

/// <summary>
/// User-supplied transfer request payload.
/// </summary>
public class TransferRequest
{
    public int sourceProductId { get; set; }
    public int targetStoreId { get; set; }
    /// <summary>"clone" or "move".</summary>
    public string mode { get; set; } = "clone";
    /// <summary>Optional SKU override (used when target store has a collision).</summary>
    public string? newSku { get; set; }
    /// <summary>"match" | "use" | "create" | "none".</summary>
    public string categoryAction { get; set; } = "match";
    public int? categoryId { get; set; }
    /// <summary>"none" | "use" | "create".</summary>
    public string groupAction { get; set; } = "none";
    public int? groupId { get; set; }
    public string? groupName { get; set; }
    /// <summary>Required true if source has marketplace links - confirms user understands they will be broken.</summary>
    public bool confirmBreakLinks { get; set; }
    /// <summary>When true and target store already has a category with the same name but missing
    /// attributes, the missing attribute definitions are added to the existing category.
    /// When false, the existing category is used as-is and unmappable values are silently dropped.</summary>
    public bool syncMissingCategoryAttributes { get; set; }
}

public class TransferResult
{
    public bool success { get; set; }
    public string mode { get; set; } = "";
    public int newProductId { get; set; }
    public int targetStoreId { get; set; }
    public int filesCopied { get; set; }
    public int filesFailed { get; set; }
    public int imagesCopied { get; set; }
    public int notesCopied { get; set; }
    public int variantsCopied { get; set; }
    public int attributeValuesCopied { get; set; }
    public int storeAttributeValuesCopied { get; set; }
    public int marketplaceLinksBroken { get; set; }
    public bool originalSoftDeleted { get; set; }
}

public class TransferPreview
{
    public SourceProductInfo sourceProduct { get; set; } = new();
    public TargetStoreInfo targetStore { get; set; } = new();
    public bool skuConflict { get; set; }
    public MatchedCategoryInfo? matchedCategory { get; set; }
    public int storeAttributesMappable { get; set; }
    public List<string> storeAttributesUnmappable { get; set; } = new();
    public List<BrokenLinkPreview> marketplaceLinksToBreak { get; set; } = new();
    public List<GroupOption> targetGroups { get; set; } = new();
    public int filesCount { get; set; }
    public int foldersCount { get; set; }
    public int imagesCount { get; set; }
    public int notesCount { get; set; }
    public int variantsCount { get; set; }
}

public class SourceProductInfo
{
    public int id { get; set; }
    public string sku { get; set; } = "";
    public string name { get; set; } = "";
    public string? categoryName { get; set; }
}

public class TargetStoreInfo
{
    public int id { get; set; }
    public string name { get; set; } = "";
}

public class MatchedCategoryInfo
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public int attributesMappable { get; set; }
    public List<string> attributesUnmappable { get; set; } = new();
}

public class BrokenLinkPreview
{
    public int connectionId { get; set; }
    public string connectionName { get; set; } = "";
    public string externalProductId { get; set; } = "";
    public string status { get; set; } = "";
}

public class GroupOption
{
    public int id { get; set; }
    public string name { get; set; } = "";
}

#endregion
