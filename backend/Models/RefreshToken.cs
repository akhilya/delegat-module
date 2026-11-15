using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models;

public class RefreshToken : IEntity
{
    public int Id { get; set; }
    public int userId { get; set; }
    public string token { get; set; } = null!;
    public DateTime expiresAt { get; set; }
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime? revokedAt { get; set; }
    /// <summary>Id of the token this one was rotated into; null when revoked by logout or reuse detection.</summary>
    public int? replacedByTokenId { get; set; }
    public bool IsExpired => DateTime.UtcNow >= expiresAt;
    public bool IsRevoked => revokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    /// <summary>How long a rotated token may still resolve to its replacement (concurrent tabs).</summary>
    public static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(30);

    /// <summary>True when this token was rotated (not logged out) and is still inside the grace window.</summary>
    public bool IsWithinRotationGrace(DateTime utcNow) =>
        revokedAt != null
        && replacedByTokenId != null
        && utcNow - revokedAt.Value <= RotationGracePeriod;

    // Navigation
    public User user { get; set; } = null!;
}
