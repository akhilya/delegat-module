using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Per-store white-label branding. Seeded from BRAND_* env when the row is first created;
/// afterwards the admin UI is the source of truth.
/// </summary>
public class StoreBrandingSettings
{
    public const string DefaultName = "Delegat.Module";

    /// <summary>Control Plane square favicon shipped in instance <c>frontend/public</c>.</summary>
    public const string DefaultFaviconUrl = "/logo-square-favicon.webp";

    /// <summary>Pre-white-label static logo path — treat as “no logo” when resolving.</summary>
    public const string LegacyDefaultLogoUrl = "/logo@0,25x.webp";

    /// <summary>Pre-white-label static favicon path — treat as CP default when resolving.</summary>
    public const string LegacyDefaultFaviconUrl = "/logo_sm@0,15x.webp";

    [Key]
    public int Id { get; set; }

    public int StoreId { get; set; }

    [ForeignKey(nameof(StoreId))]
    public Store? Store { get; set; }

    [MaxLength(120)]
    public string name { get; set; } = DefaultName;

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
