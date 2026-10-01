using Delegat.Models.Products;

namespace Delegat.Infrastructure;

/// <summary>
/// Fills hasMapping / mappingMissing on product-list adapterLinks.
/// Extracted so ProductReviewDTO (struct) write-back can be unit-tested.
/// </summary>
public static class ProductAdapterLinkMappingEnrichment
{
    /// <summary>
    /// Mutates <paramref name="dtos"/> in place (must reassign struct elements).
    /// </summary>
    public static void Apply(
        List<Product.ProductReviewDTO> dtos,
        IReadOnlyDictionary<(int categoryId, int connectionId), int> mappingCounts,
        IReadOnlyDictionary<int, Product> productById)
    {
        for (var i = 0; i < dtos.Count; i++)
        {
            var dto = dtos[i];
            if (dto.adapterLinks == null)
                continue;

            productById.TryGetValue(dto.id, out var product);
            var enriched = new List<Product.AdapterLinkSummary>();
            foreach (var link in dto.adapterLinks)
            {
                var count = 0;
                if (dto.categoryId is int categoryId)
                    mappingCounts.TryGetValue((categoryId, link.connectionId), out count);
                var hasMapping = count > 0;
                var entityLink = product?.marketplaceLinks?
                    .FirstOrDefault(l => l.Id == link.linkId);
                var savedMappingWasDeleted = entityLink != null
                    && entityLink.categoryMappingId != null
                    && entityLink.categoryMapping == null;
                var savedMatches = entityLink?.categoryMapping != null
                    && dto.categoryId.HasValue
                    && entityLink.categoryMapping.localCategoryId == dto.categoryId.Value;
                // Several mappings and none chosen (including a choice left over from another category).
                var mappingUnselected = count > 1 && !savedMatches;

                var copy = link;
                copy.hasMapping = hasMapping;
                copy.mappingUnselected = mappingUnselected;
                copy.mappingMissing = !mappingUnselected
                    && (!hasMapping || savedMappingWasDeleted || link.mappingMissing);
                enriched.Add(copy);
            }

            dto.adapterLinks = enriched;
            // ProductReviewDTO is a struct — write the copy back into the list.
            dtos[i] = dto;
        }
    }
}
