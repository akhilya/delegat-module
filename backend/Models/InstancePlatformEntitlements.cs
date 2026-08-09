using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Cached Platform Link entitlements (singleton Id = 1) for admin UI when CP is briefly unreachable.
/// </summary>
public class InstancePlatformEntitlements
{
    public const int SingletonId = 1;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = SingletonId;

    public bool linked { get; set; }

    [StringLength(128)]
    public string? planName { get; set; }

    [StringLength(64)]
    public string? planSlug { get; set; }

    [StringLength(32)]
    public string? subscriptionStatus { get; set; }

    public DateTimeOffset? overagePurgeAt { get; set; }

    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
}
