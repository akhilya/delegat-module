using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Delegat.Enums;

namespace Delegat.Models.Marketplace;

/// <summary>
/// Links an evaluated grouping VALUE (computed by the mapping graph's grouping field,
/// e.g. "мифология") to a marketplace group id (e.g. WB imtID) for endpoint-based grouping.
/// Products whose mapping evaluates to the same value end up on one marketplace card,
/// regardless of which local ProductGroup they belong to.
/// </summary>
public class GroupValueMarketplaceLink
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(connection))]
    public int connectionId { get; set; }
    public MarketplaceConnection connection { get; set; } = null!;

    /// <summary>Evaluated grouping value (trimmed). Unique per connection.</summary>
    [StringLength(300)]
    public string groupValue { get; set; } = null!;

    /// <summary>Marketplace group identifier (e.g. WB imtID).</summary>
    [StringLength(100)]
    public string externalGroupId { get; set; } = null!;

    public ProductMarketplaceStatus status { get; set; } = ProductMarketplaceStatus.Synced;

    public string? lastErrorMessage { get; set; }

    public DateTime? lastSyncAt { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}
