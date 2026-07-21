namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Represents a product variant (e.g. size) within a marketplace card.
/// Used for WB-style marketplaces where one card has multiple sizes (chrtIds).
/// </summary>
/// <param name="VariantId">Variant identifier on marketplace (e.g. chrtId for WB).</param>
/// <param name="TechSize">Technical size label (e.g. "S", "M", "L", "0").</param>
/// <param name="MarketplaceSize">Marketplace-specific size label (e.g. "42", "44" for WB wbSize).</param>
/// <param name="Barcodes">Barcodes associated with this variant (e.g. WB skus[]).</param>
/// <param name="Price">Price for this variant (null = use product-level price).</param>
/// <param name="Quantity">Stock quantity for this variant.</param>
/// <param name="Extra">Additional variant-specific data.</param>
public record ExternalProductVariant(
    string VariantId,
    string? TechSize = null,
    string? MarketplaceSize = null,
    List<string>? Barcodes = null,
    decimal? Price = null,
    int? Quantity = null,
    Dictionary<string, object>? Extra = null
);
