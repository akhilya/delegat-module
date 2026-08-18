namespace Delegat.Services.Marketplace;

public enum CatalogMatchStatus
{
    Matched,
    Ambiguous,
    Unmatched,
}

/// <summary>
/// Pure barcode-then-SKU matcher. No name matching.
/// </summary>
public static class CatalogMatchEngine
{
    public sealed record LocalProduct(int ProductId, string? Sku, string? Barcode, string? Name);

    public sealed record RemoteProduct(string ExternalId, string? Sku, string? Barcode, string? Name);

    public sealed record Candidate(int ProductId, string? Sku, string? Name);

    public sealed record MatchRow(
        string ExternalId,
        string? Name,
        string? Sku,
        string? Barcode,
        CatalogMatchStatus Status,
        int? ProductId,
        string? ProductName,
        string? ProductSku,
        string? MatchedBy,
        IReadOnlyList<Candidate> Candidates);

    public static IReadOnlyList<MatchRow> Match(
        IReadOnlyList<LocalProduct> locals,
        IReadOnlyList<RemoteProduct> remotes)
    {
        var byBarcode = new Dictionary<string, List<LocalProduct>>(StringComparer.Ordinal);
        var bySku = new Dictionary<string, List<LocalProduct>>(StringComparer.Ordinal);

        foreach (var local in locals)
        {
            var barcode = MarketplaceImportBarcode.NormalizeIfValid(local.Barcode);
            if (barcode != null)
                Add(byBarcode, barcode, local);

            var sku = NormalizeSku(local.Sku);
            if (sku != null)
                Add(bySku, sku, local);
        }

        var rows = new List<MatchRow>(remotes.Count);
        foreach (var remote in remotes)
        {
            var barcode = MarketplaceImportBarcode.NormalizeIfValid(remote.Barcode);
            if (barcode != null && byBarcode.TryGetValue(barcode, out var barcodeHits) && barcodeHits.Count > 0)
            {
                rows.Add(BuildRow(remote, barcodeHits, "barcode"));
                continue;
            }

            var sku = NormalizeSku(remote.Sku);
            if (sku != null && bySku.TryGetValue(sku, out var skuHits) && skuHits.Count > 0)
            {
                rows.Add(BuildRow(remote, skuHits, "sku"));
                continue;
            }

            rows.Add(new MatchRow(
                remote.ExternalId,
                remote.Name,
                remote.Sku,
                remote.Barcode,
                CatalogMatchStatus.Unmatched,
                null,
                null,
                null,
                null,
                Array.Empty<Candidate>()));
        }

        return rows;
    }

    private static MatchRow BuildRow(RemoteProduct remote, List<LocalProduct> hits, string matchedBy)
    {
        var candidates = hits
            .Select(h => new Candidate(h.ProductId, h.Sku, h.Name))
            .ToList();

        if (hits.Count == 1)
        {
            var hit = hits[0];
            return new MatchRow(
                remote.ExternalId,
                remote.Name,
                remote.Sku,
                remote.Barcode,
                CatalogMatchStatus.Matched,
                hit.ProductId,
                hit.Name,
                hit.Sku,
                matchedBy,
                candidates);
        }

        return new MatchRow(
            remote.ExternalId,
            remote.Name,
            remote.Sku,
            remote.Barcode,
            CatalogMatchStatus.Ambiguous,
            null,
            null,
            null,
            matchedBy,
            candidates);
    }

    public static string? NormalizeSku(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Trim().ToUpperInvariant();
    }

    private static void Add(Dictionary<string, List<LocalProduct>> map, string key, LocalProduct local)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(local);
    }
}
