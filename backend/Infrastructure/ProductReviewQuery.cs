using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Infrastructure;

/// <summary>Only materialize fields used by catalog rows, never mapping graphs or credentials.</summary>
public static class ProductReviewQuery
{
    public static IQueryable<Product> SelectReviewData(this IQueryable<Product> query) => query
        .AsNoTracking()
        .AsSplitQuery()
        .Select(p => new Product
        {
            Id = p.Id,
            sku = p.sku,
            barcode = p.barcode,
            name = p.name,
            categoryId = p.categoryId,
            storageQuantity = p.storageQuantity,
            price = p.price,
            createdAt = p.createdAt,
            updatedAt = p.updatedAt,
            deletedAt = p.deletedAt,
            category = p.category == null ? null : new Category { Id = p.category.Id, name = p.category.name },
            files = p.files.Where(f => f.contentType.StartsWith("image/"))
                .OrderBy(f => f.Id).Take(1)
                .Select(f => new Models.File { Id = f.Id, contentType = f.contentType }).ToList(),
            images = p.images.Where(i => i.file.contentType.StartsWith("image/"))
                .OrderBy(i => i.index).ThenBy(i => i.Id).Take(1)
                .Select(i => new ImageReference
                {
                    Id = i.Id,
                    index = i.index,
                    fileId = i.fileId,
                    file = new Models.File { Id = i.file.Id, contentType = i.file.contentType }
                }).ToList(),
            storeAttributeValues = p.storeAttributeValues.Select(v => new StoreProductAttributeValue
            {
                Id = v.Id,
                storeAttributeId = v.storeAttributeId,
                value = v.value,
                storeAttribute = new StoreAttribute
                {
                    Id = v.storeAttribute.Id,
                    key = v.storeAttribute.key,
                    name = v.storeAttribute.name,
                    type = v.storeAttribute.type
                },
                files = v.files.Select(f => new StoreProductAttributeFile
                {
                    Id = f.Id,
                    fileId = f.fileId,
                    index = f.index
                }).ToList()
            }).ToList(),
            marketplaceLinks = p.marketplaceLinks.Select(l => new ProductMarketplaceLink
            {
                Id = l.Id,
                connectionId = l.connectionId,
                categoryMappingId = l.categoryMappingId,
                status = l.status,
                externalProductId = l.externalProductId,
                externalSku = l.externalSku,
                lastSyncAt = l.lastSyncAt,
                preferredExportMode = l.preferredExportMode,
                connection = new MarketplaceConnection { Id = l.connection.Id, adapterKey = l.connection.adapterKey },
                categoryMapping = l.categoryMapping == null ? null : new CategoryMapping
                {
                    Id = l.categoryMapping.Id,
                    localCategoryId = l.categoryMapping.localCategoryId
                }
            }).ToList()
        });
}
