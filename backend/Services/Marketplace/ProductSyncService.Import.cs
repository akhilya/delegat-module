using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using ExternalMedia = Delegat.Marketplace.SDK.DTOs.ExternalMedia;

namespace Delegat.Services.Marketplace;

public partial class ProductSyncService
{
    private static string? PublicImportReceiptKey(MarketplaceConnection connection, string externalId, string? operationId)
        => string.IsNullOrWhiteSpace(operationId) ? null : Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { connection.storeId, connection.Id, externalId, operationId }))));

    private async Task<ProductImportResult?> FindCommittedImportAsync(MarketplaceConnection connection,
        string externalId, bool linked, string? receiptKey, CancellationToken ct)
    {
        if (linked)
        {
            var existing = await _context.productMarketplaceLinks.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.connectionId == connection.Id && l.externalProductId == externalId
                    && l.product.storeId == connection.storeId)
                .Select(l => new { l.productId, l.product.name }).FirstOrDefaultAsync(ct);
            return existing == null ? null : ProductImportResult.AlreadyExists(existing.productId, existing.name);
        }
        if (receiptKey == null) return null;
        var receipt = await _context.Set<ProductImportReceipt>().IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.key == receiptKey && r.product.storeId == connection.storeId)
            .Select(r => new { r.productId, r.product.name }).SingleOrDefaultAsync(ct);
        return receipt == null ? null : ProductImportResult.SuccessUnlinked(receipt.productId, receipt.name,
            "Product was already copied by this operation");
    }

    /// <summary>
    /// Prepare a connected entity graph without writing or calling external services.
    /// A single SaveChanges atomically commits the card, attributes, variants, links,
    /// group, media queue and optional retry receipt under EF's relational transaction.
    /// </summary>
    private async Task<ProductImportResult> CommitNewImportAsync(MarketplaceConnection connection,
        ExternalProduct external, string externalId, int? categoryId, int? authorId,
        string? mediaBatchId, bool linked, string? receiptKey, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var existing = await FindCommittedImportAsync(connection, externalId, linked, receiptKey, ct);
            if (existing != null) return existing;
            var before = _context.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
            try
            {
                var sku = linked ? StringClip.ClipRequired(external.Sku, Product.PRODUCT_SKU_MAX_LENGTH, $"MP-{externalId}")
                    : await ResolveUniqueSkuAsync(connection.storeId, external.Sku, externalId, connection.adapterKey, ct);
                var product = new Product();
                product.AddFill(new Product.AddForm(new Product.UserAddForm
                {
                    sku = sku,
                    barcode = StringClip.ClipOptional(MarketplaceImportBarcode.Resolve(external), Product.PRODUCT_BARCODE_MAX_LENGTH),
                    name = StringClip.ClipRequired(external.Name, Product.PRODUCT_NAME_MAX_LENGTH, $"Product {externalId}"),
                    description = StringClip.Clip(Delegat.Marketplace.SDK.DescriptionFormatting.FromMarketplaceHtml(external.Description), Product.PRODUCT_DESCRIPTION_MAX_LENGTH),
                    price = external.Price,
                    storageQuantity = external.Quantity
                }, connection.storeId, categoryId));

                // A variant owns its barcode. The card cannot claim the same code in
                // the shared store namespace (including the legacy variant fallback).
                if (product.barcode != null && (external.Variants ?? []).Any(v =>
                    string.Equals(v.Barcodes?.FirstOrDefault()?.Trim(), product.barcode, StringComparison.Ordinal)))
                    product.barcode = null;

                var mappings = categoryId.HasValue ? await _context.categoryMappings.AsNoTracking()
                    .Where(m => m.connectionId == connection.Id && m.localCategoryId == categoryId.Value)
                    .OrderBy(m => m.Id).ToListAsync(ct) : [];
                var attributeMedia = new List<PendingImageDownload>();
                if (categoryId.HasValue && mappings.Count == 1)
                {
                    attributeMedia = await PrepareNewImportAttributesAsync(
                        product,
                        mappings[0],
                        ImportedAttributeValue.Collect(external),
                        authorId,
                        mediaBatchId,
                        external.Media,
                        ct);
                }

                ProductMarketplaceLink? link = null;
                if (linked)
                {
                    link = new ProductMarketplaceLink();
                    link.AddFill(new ProductMarketplaceLink.AddForm
                    {
                        connectionId = connection.Id,
                        externalProductId = externalId,
                        externalSku = external.Sku,
                        status = ProductMarketplaceStatus.Synced,
                        categoryMappingId = mappings.FirstOrDefault()?.Id
                    });
                    link.externalPrice = external.Price;
                    link.externalStock = external.Quantity;
                    link.lastSyncAt = DateTime.UtcNow;
                    product.marketplaceLinks.Add(link);
                }
                PrepareNewImportVariants(product, connection.Id, external.Variants ?? [], linked);
                if (linked && !string.IsNullOrEmpty(external.GroupId))
                    await PrepareNewImportGroupAsync(product, connection.Id, external.GroupId, external.GroupName, ct);

                var galleryMedia = authorId.HasValue
                    ? PrepareNewImportMedia(product, external, authorId.Value, mediaBatchId) : [];
                var media = MergeNewImportMedia(galleryMedia, attributeMedia);
                await _quotaService.EnsureProductQuotaAsync(connection.storeId);
                ct.ThrowIfCancellationRequested();
                _context.products.Add(product);
                _context.pendingImageDownloads.AddRange(media);
                if (receiptKey != null)
                    _context.Set<ProductImportReceipt>().Add(new ProductImportReceipt { key = receiptKey, product = product });

                await _context.SaveChangesAsync(ct);
                return linked ? ProductImportResult.Success(product.Id, link!.Id, product.name, "Product imported successfully")
                    : ProductImportResult.SuccessUnlinked(product.Id, product.name, "Product copied from the public catalog");
            }
            catch (DbUpdateException) when (attempt == 0 && !ct.IsCancellationRequested)
            {
                // A concurrent import may have won the unique product/group/receipt key.
                // Rebuild from committed state once, never replay external requests.
            }
            finally
            {
                foreach (var entry in _context.ChangeTracker.Entries().Where(e => !before.Contains(e.Entity)).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<List<PendingImageDownload>> PrepareNewImportAttributesAsync(
        Product product,
        CategoryMapping mapping,
        Dictionary<string, object> external,
        int? authorId,
        string? mediaBatchId,
        IReadOnlyList<ExternalMedia>? externalMedia,
        CancellationToken ct)
    {
        var downloads = new List<PendingImageDownload>();
        if (external.Count == 0) return downloads;
        var fields = MappingService.ProjectGraphForImport(mapping.graphJson);
        var byKey = new Dictionary<string, object>(external, StringComparer.OrdinalIgnoreCase);
        var (category, store) = await LoadAttributeSpecsAsync(mapping.localCategoryId, noTracking: true, ct);
        foreach (var field in fields)
        {
            if (!field.sourceAttributeId.HasValue || !ImportedAttributeValue.TryGet(byKey,
                field.externalFieldKey, field.externalFieldName, out var externalValue)) continue;
            var id = field.sourceAttributeId.Value;
            if (field.sourceType == FieldMappingSourceType.CategoryAttribute && category.TryGetValue(id, out var categorySpec))
            {
                if (categorySpec.Type == AttributeType.File)
                {
                    if (authorId.HasValue)
                    {
                        foreach (var url in GetImportedFileUrls(externalValue, categorySpec))
                        {
                            var (kind, fileName) = GetImportedMediaMetadata(url, externalMedia);
                            downloads.Add(new PendingImageDownload
                            {
                                product = product,
                                storeId = product.storeId,
                                authorUserId = authorId.Value,
                                imageUrl = url,
                                imageIndex = downloads.Count,
                                mediaKind = kind,
                                mediaFileName = fileName,
                                targetAttributeId = id,
                                skipGallery = true,
                                batchId = mediaBatchId
                            });
                        }
                    }
                    continue;
                }

                var value = FormatImportedAttribute(externalValue, categorySpec, field.modifiersJson);
                if (!string.IsNullOrEmpty(value))
                    product.attributeValues.Add(new ProductAttributeValue { attributeId = id, value = value });
            }
            else if (field.sourceType == FieldMappingSourceType.StoreAttribute && store.TryGetValue(id, out var storeSpec))
            {
                if (storeSpec.Type == AttributeType.File)
                {
                    if (authorId.HasValue)
                    {
                        foreach (var url in GetImportedFileUrls(externalValue, storeSpec))
                        {
                            var (kind, fileName) = GetImportedMediaMetadata(url, externalMedia);
                            downloads.Add(new PendingImageDownload
                            {
                                product = product,
                                storeId = product.storeId,
                                authorUserId = authorId.Value,
                                imageUrl = url,
                                imageIndex = downloads.Count,
                                mediaKind = kind,
                                mediaFileName = fileName,
                                targetStoreAttributeId = id,
                                skipGallery = true,
                                batchId = mediaBatchId
                            });
                        }
                    }
                    continue;
                }

                var value = FormatImportedAttribute(externalValue, storeSpec, field.modifiersJson);
                if (!string.IsNullOrEmpty(value))
                    product.storeAttributeValues.Add(new StoreProductAttributeValue { storeAttributeId = id, value = value });
            }
        }

        return downloads;
    }

    private static void PrepareNewImportVariants(Product product, int connectionId,
        List<ExternalProductVariant> variants, bool linked)
    {
        foreach (var source in variants)
        {
            var barcode = source.Barcodes?.FirstOrDefault();
            var variant = product.variants.FirstOrDefault(v =>
                (!string.IsNullOrEmpty(barcode) && v.barcode == barcode) ||
                (!string.IsNullOrEmpty(source.TechSize) && v.size == source.TechSize));
            if (variant == null)
            {
                variant = new ProductVariant();
                variant.AddFill(new ProductVariant.AddForm
                {
                    barcode = barcode,
                    size = source.TechSize,
                    price = source.Price,
                    quantity = source.Quantity,
                    sortOrder = product.variants.Count
                });
                product.variants.Add(variant);
            }
            else
            {
                if (source.Price.HasValue) variant.price = source.Price;
                if (source.Quantity.HasValue) variant.quantity = source.Quantity;
                if (string.IsNullOrEmpty(variant.barcode)) variant.barcode = barcode;
            }
            if (!linked) continue;
            var link = variant.marketplaceLinks.FirstOrDefault();
            if (link == null)
            {
                link = new ProductVariantLink { connectionId = connectionId };
                variant.marketplaceLinks.Add(link);
            }
            link.externalVariantId = source.VariantId;
            link.marketplaceSize = source.MarketplaceSize;
            link.rawData = source.Extra == null ? null : JsonSerializer.Serialize(source.Extra);
        }
    }

    private async Task PrepareNewImportGroupAsync(Product product, int connectionId, string externalId,
        string? externalName, CancellationToken ct)
    {
        var existing = await _context.productGroupMarketplaceLinks.AsNoTracking()
            .Where(l => l.connectionId == connectionId && l.externalGroupId == externalId)
            .Select(l => new { l.productGroupId }).SingleOrDefaultAsync(ct);
        var member = new ProductGroupMember();
        if (existing != null)
        {
            member.groupId = existing.productGroupId;
            member.sortOrder = (await _context.productGroupMembers.Where(m => m.groupId == existing.productGroupId)
                .MaxAsync(m => (int?)m.sortOrder, ct) ?? 0) + 1;
        }
        else
        {
            var group = new ProductGroup();
            group.AddFill(new ProductGroup.AddForm
            {
                storeId = product.storeId,
                name = !string.IsNullOrEmpty(externalName) ? externalName : $"Group {externalId}",
                description = $"Automatically created during import (externalGroupId: {externalId})"
            });
            group.marketplaceLinks.Add(new ProductGroupMarketplaceLink
            {
                connectionId = connectionId,
                externalGroupId = externalId,
                externalGroupName = externalName,
                status = ProductMarketplaceStatus.Synced
            });
            member.group = group;
            member.sortOrder = 1;
        }
        product.groupMemberships.Add(member);
    }

    private static List<PendingImageDownload> PrepareNewImportMedia(Product product, ExternalProduct external,
        int authorId, string? batchId)
    {
        var rows = new List<PendingImageDownload>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string url, PendingMediaKind kind, string? name = null)
        {
            var key = ImageSourceUrl.Normalize(url) ?? url.Trim();
            if (!seen.Add(key)) return;
            rows.Add(new PendingImageDownload
            {
                product = product,
                storeId = product.storeId,
                authorUserId = authorId,
                imageUrl = url.Trim(),
                imageIndex = rows.Count,
                mediaKind = kind,
                mediaFileName = SafeMediaFileName(name),
                batchId = batchId
            });
        }
        foreach (var url in (external.ImageUrls ?? []).Concat((external.Media ?? [])
            .Where(m => string.Equals(m.Kind, "image", StringComparison.OrdinalIgnoreCase)).Select(m => m.Url)))
            if (!string.IsNullOrWhiteSpace(url)) Add(url, PendingMediaKind.Image);
        foreach (var media in (external.Media ?? []).Where(m => string.Equals(m.Kind, "video", StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.IsNullOrWhiteSpace(m.Url) && UrlSafety.TryValidateHttpUrl(m.Url, out _, out _))
            .DistinctBy(m => ImageSourceUrl.Normalize(m.Url) ?? m.Url.Trim()).Take(100))
            Add(media.Url, PendingMediaKind.Video, media.FileName);
        return rows;
    }

    private static List<PendingImageDownload> MergeNewImportMedia(
        List<PendingImageDownload> galleryRows,
        List<PendingImageDownload> attributeRows)
    {
        foreach (var attributeRow in attributeRows)
        {
            var galleryRow = galleryRows.FirstOrDefault(row =>
                !row.targetAttributeId.HasValue
                && !row.targetStoreAttributeId.HasValue
                && string.Equals(
                    row.imageUrl.Trim(),
                    attributeRow.imageUrl.Trim(),
                    StringComparison.Ordinal));
            if (galleryRow != null)
            {
                galleryRow.targetAttributeId = attributeRow.targetAttributeId;
                galleryRow.targetStoreAttributeId = attributeRow.targetStoreAttributeId;
                continue;
            }

            attributeRow.imageIndex = galleryRows.Count;
            galleryRows.Add(attributeRow);
        }

        return galleryRows;
    }
}
