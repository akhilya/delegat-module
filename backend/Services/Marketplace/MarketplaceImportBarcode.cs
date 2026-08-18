using Delegat.Controllers;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Resolves a local product EAN barcode from marketplace import payloads.
/// Only valid EAN-8 / EAN-13 values that fit <see cref="Product.PRODUCT_BARCODE_MAX_LENGTH"/> are accepted.
/// </summary>
public static class MarketplaceImportBarcode
{
    /// <summary>
    /// Picks the first valid product-level barcode, then falls back to variant barcodes.
    /// </summary>
    public static string? Resolve(ExternalProduct external)
    {
        foreach (var candidate in EnumerateCandidates(external))
        {
            var normalized = Normalize(candidate);
            if (normalized != null)
                return normalized;
        }

        return null;
    }

    /// <summary>
    /// True when import would change the local barcode (external has a valid different value).
    /// </summary>
    public static bool WouldChange(Product product, ExternalProduct external, out string? oldValue, out string? newValue)
    {
        oldValue = product.barcode;
        newValue = Resolve(external);
        return newValue != null && !string.Equals(oldValue, newValue, StringComparison.Ordinal);
    }

    private static IEnumerable<string?> EnumerateCandidates(ExternalProduct external)
    {
        yield return external.Barcode;

        if (external.Variants == null)
            yield break;

        foreach (var variant in external.Variants)
        {
            if (variant.Barcodes == null)
                continue;

            foreach (var barcode in variant.Barcodes)
                yield return barcode;
        }
    }

    /// <summary>
    /// Returns a trimmed valid EAN-8/13, or null.
    /// </summary>
    public static string? NormalizeIfValid(string? raw) => Normalize(raw);

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed.Length > Product.PRODUCT_BARCODE_MAX_LENGTH)
            return null;

        return ProductController.IsValidEanBarcode(trimmed) ? trimmed : null;
    }
}
