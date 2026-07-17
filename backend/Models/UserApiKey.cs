using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// API key bound to a user for Public API v1 access.
/// Plaintext is shown once at creation; only the hash is stored.
/// </summary>
public class UserApiKey
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(user))]
    public int userId { get; set; }

    public User user { get; set; } = null!;

    /// <summary>Human-readable label (e.g. "1C integration").</summary>
    [StringLength(100)]
    public string name { get; set; } = null!;

    /// <summary>Public prefix for UI display (e.g. "dk_").</summary>
    [StringLength(16)]
    public string keyPrefix { get; set; } = "dk_";

    /// <summary>SHA-256 hex hash of the full plaintext key.</summary>
    [StringLength(64)]
    public string keyHash { get; set; } = null!;

    /// <summary>Last 4 characters of the key for UI identification.</summary>
    [StringLength(4)]
    public string last4 { get; set; } = null!;

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public DateTime? lastUsedAt { get; set; }

    public DateTime? revokedAt { get; set; }

    [NotMapped]
    public bool isRevoked => revokedAt != null;
}
