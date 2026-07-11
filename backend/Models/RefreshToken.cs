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
    public bool IsExpired => DateTime.UtcNow >= expiresAt;
    public bool IsRevoked => revokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    // Navigation
    public User user { get; set; } = null!;
}
