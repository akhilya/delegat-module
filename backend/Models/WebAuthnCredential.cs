using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>Discoverable WebAuthn credential (passkey) bound to a user.</summary>
public class WebAuthnCredential
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(user))]
    public int userId { get; set; }

    public User user { get; set; } = null!;

    public byte[] credentialId { get; set; } = [];

    public byte[] publicKey { get; set; } = [];

    public byte[] userHandle { get; set; } = [];

    public uint signCount { get; set; }

    [StringLength(128)]
    public string? nickname { get; set; }

    public DateTimeOffset createdAt { get; set; }

    public DateTimeOffset? lastUsedAt { get; set; }
}
