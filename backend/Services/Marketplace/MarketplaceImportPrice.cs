using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Fills <see cref="ExternalProduct.Price"/> from the marketplace prices API when the
/// product card payload does not include a selling price (Wildberries content cards,
/// some Ozon / Yandex offer mappings).
/// </summary>
internal static class MarketplaceImportPrice
{
    public static bool HasSellingPrice(decimal? price) => price is > 0;

    public static ExternalProduct Apply(ExternalProduct product, PriceInfo? info)
    {
        if (HasSellingPrice(product.Price))
            return product;

        // Selling price, then discounted, then list/old (WB maps discounted → Price and list → OldPrice).
        var selling = FirstSellingPrice(info?.Price, info?.DiscountedPrice, info?.OldPrice);
        if (!HasSellingPrice(selling))
            return product;

        return product with { Price = selling };
    }

    private static decimal? FirstSellingPrice(params decimal?[] values)
    {
        foreach (var value in values)
        {
            if (HasSellingPrice(value))
                return value;
        }

        return null;
    }

    public static async Task<List<ExternalProduct>> FillMissingAsync(
        IReadOnlyList<ExternalProduct> products,
        Func<List<string>, CancellationToken, Task<List<PriceInfo>>> importPrices,
        CancellationToken ct)
    {
        if (products.Count == 0)
            return products as List<ExternalProduct> ?? products.ToList();

        var missingIds = products
            .Where(p => !HasSellingPrice(p.Price))
            .Select(p => p.ExternalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingIds.Count == 0)
            return products as List<ExternalProduct> ?? products.ToList();

        List<PriceInfo> prices;
        try
        {
            prices = await importPrices(missingIds, ct);
        }
        catch
        {
            return products as List<ExternalProduct> ?? products.ToList();
        }

        if (prices.Count == 0)
            return products as List<ExternalProduct> ?? products.ToList();

        var byId = prices
            .Where(p => !string.IsNullOrWhiteSpace(p.ExternalProductId))
            .GroupBy(p => p.ExternalProductId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        return products
            .Select(p =>
                byId.TryGetValue(p.ExternalId, out var info) ? Apply(p, info) : p)
            .ToList();
    }
}
