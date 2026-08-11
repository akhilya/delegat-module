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
        HashSet<(int categoryId, int connectionId)> mappingPairs,
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
                var hasMapping = dto.categoryId.HasValue
                    && mappingPairs.Contains((dto.categoryId.Value, link.connectionId));
                var entityLink = product?.marketplaceLinks?
                    .FirstOrDefault(l => l.Id == link.linkId);
                var savedMappingWasDeleted = entityLink != null
                    && entityLink.categoryMappingId != null
                    && entityLink.categoryMapping == null;

                var copy = link;
                copy.hasMapping = hasMapping;
                copy.mappingMissing = !hasMapping || savedMappingWasDeleted || link.mappingMissing;
                enriched.Add(copy);
            }

            dto.adapterLinks = enriched;
            // ProductReviewDTO is a struct — write the copy back into the list.
            dtos[i] = dto;
        }
    }
}
