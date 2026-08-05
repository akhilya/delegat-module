using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Singleton row (Id = 1) for instance white-label branding. Seeded from env on first boot;
/// afterwards the admin UI is the source of truth (no rebuild required).
/// </summary>
public class InstanceBrandingSettings
{
    public const int SingletonId = 1;

    public const string DefaultName = "Delegat";

    /// <summary>Control Plane square favicon shipped in instance <c>frontend/public</c>.</summary>
    public const string DefaultFaviconUrl = "/logo-square-favicon.webp";

    /// <summary>Pre-white-label static logo path — treat as “no logo” when resolving.</summary>
    public const string LegacyDefaultLogoUrl = "/logo@0,25x.webp";

    /// <summary>Pre-white-label static favicon path — treat as CP default when resolving.</summary>
    public const string LegacyDefaultFaviconUrl = "/logo_sm@0,15x.webp";

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = SingletonId;

    [MaxLength(120)]
    public string name { get; set; } = DefaultName;

    [MaxLength(500)]
    public string? description { get; set; }

    /// <summary>MinIO object key for uploaded logo; null uses <see cref="logoUrl"/>.</summary>
    [MaxLength(512)]
    public string? logoObjectKey { get; set; }

    [MaxLength(128)]
    public string? logoContentType { get; set; }

    /// <summary>Static or external logo URL when no object key; null/empty = no logo.</summary>
    [MaxLength(1024)]
    public string? logoUrl { get; set; }

    [MaxLength(512)]
    public string? faviconObjectKey { get; set; }

    [MaxLength(128)]
    public string? faviconContentType { get; set; }

    [MaxLength(1024)]
    public string? faviconUrl { get; set; }

    /// <summary>Primary brand color as #RRGGBB; null/empty = design default.</summary>
    [MaxLength(7)]
    public string? primaryColor { get; set; }

    /// <summary>Accent brand color as #RRGGBB; null/empty = design default.</summary>
    [MaxLength(7)]
    public string? accentColor { get; set; }

    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
}
