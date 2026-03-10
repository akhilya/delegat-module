using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PrintO.Infrastructure.Interfaces;

namespace PrintO.Models;

public class FolderShareToken : IEntity
{
    [Key]
    public int Id { get; set; }

    public int productId { get; set; }

    /// <summary>
    /// Nullable folder ID. Null means root (all product files).
    /// </summary>
    public int? folderId { get; set; }

    public string token { get; set; } = null!;

    public DateTime expiresAt { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(createdByUser))]
    public int createdByUserId { get; set; }

    public bool IsExpired => DateTime.UtcNow >= expiresAt;

    // Navigation
    [ForeignKey(nameof(productId))]
    public Products.Product product { get; set; } = null!;

    [ForeignKey(nameof(folderId))]
    public ProductFolder? folder { get; set; }

    public User createdByUser { get; set; } = null!;
}
