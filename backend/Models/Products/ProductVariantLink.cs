using Delegat.Infrastructure.Interfaces;
using Delegat.Models.Marketplace;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Products;

/// <summary>
/// Maps a local ProductVariant to its marketplace-specific identifier.
/// One variant can have links to multiple marketplace connections.
/// </summary>
public class ProductVariantLink : IEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the local variant.
    /// </summary>
    [ForeignKey(nameof(variant))]
    public int variantId { get; set; }

    /// <summary>
    /// Navigation property to the local variant.
    /// </summary>
    public ProductVariant variant { get; set; } = null!;

    /// <summary>
    /// Foreign key to the marketplace connection.
    /// </summary>
    [ForeignKey(nameof(connection))]
    public int connectionId { get; set; }

    /// <summary>
    /// Navigation property to the marketplace connection.
    /// </summary>
    public MarketplaceConnection connection { get; set; } = null!;

    /// <summary>
    /// Variant identifier on the marketplace (e.g. chrtId for Wildberries).
    /// </summary>
    [StringLength(50)]
    public string externalVariantId { get; set; } = null!;

    /// <summary>
    /// Marketplace-specific size label (e.g. "42", "44" for WB wbSize).
    /// </summary>
    [StringLength(50)]
    public string? marketplaceSize { get; set; }

    /// <summary>
    /// Additional marketplace-specific data as JSON.
    /// </summary>
    [Column(TypeName = "longtext")]
    public string? rawData { get; set; }

    /// <summary>
    /// Timestamp when the link was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}
