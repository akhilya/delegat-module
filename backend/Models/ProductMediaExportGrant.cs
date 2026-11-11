using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models;

/// <summary>
/// Revocable bearer URL granting one marketplace connection access to one product video.
/// The token is intentionally stored only on this dedicated record, never on File.
/// </summary>
public sealed class ProductMediaExportGrant : IEntity
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(file))]
    public int fileId { get; set; }

    [ForeignKey(nameof(connection))]
    public int connectionId { get; set; }

    /// <summary>256-bit random URL token. Keep this value out of logs and DTOs.</summary>
    [Required]
    [StringLength(64, MinimumLength = 64)]
    public string token { get; set; } = null!;

    /// <summary>EF optimistic concurrency marker, rotated whenever grant state changes.</summary>
    [Required]
    [StringLength(32, MinimumLength = 32)]
    public string revision { get; set; } = null!;

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime? revokedAt { get; set; }

    public Models.File file { get; set; } = null!;
    public Models.Marketplace.MarketplaceConnection connection { get; set; } = null!;
}
