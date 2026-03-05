namespace PrintO.Models.Marketplace.DTOs;

/// <summary>
/// Represents a product variant (e.g. size) within a marketplace card.
/// Used for WB-style marketplaces where one card has multiple sizes (chrtIds).
/// </summary>
public record ExternalProductVariant(
    /// <summary>
    /// Variant identifier on marketplace (e.g. chrtId for WB).
    /// </summary>
    string VariantId,

    /// <summary>
    /// Technical size label (e.g. "S", "M", "L", "0").
    /// </summary>
    string? TechSize = null,

    /// <summary>
    /// Marketplace-specific size label (e.g. "42", "44" for WB wbSize).
    /// </summary>
    string? MarketplaceSize = null,

    /// <summary>
    /// Barcodes associated with this variant (e.g. WB skus[]).
    /// </summary>
    List<string>? Barcodes = null,

    /// <summary>
    /// Price for this variant (null = use product-level price).
    /// </summary>
    decimal? Price = null,

    /// <summary>
    /// Stock quantity for this variant.
    /// </summary>
    int? Quantity = null,

    /// <summary>
    /// Additional variant-specific data.
    /// </summary>
    Dictionary<string, object>? Extra = null
);
