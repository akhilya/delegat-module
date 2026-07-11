using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models;

public class StoreFileShareToken : IEntity
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(file))]
    public int fileId { get; set; }

    public string token { get; set; } = null!;

    public DateTime expiresAt { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(createdByUser))]
    public int createdByUserId { get; set; }

    public bool IsExpired => DateTime.UtcNow >= expiresAt;

    public StoreFile file { get; set; } = null!;
    public User createdByUser { get; set; } = null!;
}
