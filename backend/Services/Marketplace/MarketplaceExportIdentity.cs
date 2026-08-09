namespace Delegat.Services.Marketplace;

/// <summary>
/// Shared rules for applying adapter export ids onto ProductMarketplaceLink.
/// </summary>
public static class MarketplaceExportIdentity
{
    /// <summary>
    /// Whether a successful export may rewrite link.externalProductId.
    /// Allows quiet normalize (resolvedFrom == previous) but blocks silent rebind to another id.
    /// </summary>
    public static bool MayUpdateExternalProductId(
        string? previousLinkExternalId,
        string? pinnedExternalIdPassedToAdapter,
        string? resultExternalId,
        string? resolvedFrom)
    {
        if (string.IsNullOrWhiteSpace(resultExternalId) || resultExternalId == "0")
            return false;

        // New listing (no pin) — always allow.
        if (string.IsNullOrWhiteSpace(pinnedExternalIdPassedToAdapter))
            return true;

        if (string.Equals(resultExternalId, previousLinkExternalId, StringComparison.Ordinal))
            return true;

        // Quiet normalize: stored value was sku/offer_id, result is canonical marketplace id.
        if (!string.IsNullOrWhiteSpace(resolvedFrom)
            && string.Equals(resolvedFrom, previousLinkExternalId, StringComparison.Ordinal)
            && string.Equals(resolvedFrom, pinnedExternalIdPassedToAdapter, StringComparison.Ordinal))
            return true;

        if (string.Equals(resultExternalId, pinnedExternalIdPassedToAdapter, StringComparison.Ordinal))
            return true;

        return false;
    }

    public static string? TryGetDetailString(IReadOnlyDictionary<string, object>? details, string key)
    {
        if (details == null)
            return null;
        if (!details.TryGetValue(key, out var raw) || raw == null)
            return null;
        var s = raw.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
