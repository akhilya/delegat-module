using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PrintO.Infrastructure.Interfaces;

namespace PrintO.Models;

public class StoreFolderShareToken : IEntity
{
    [Key]
    public int Id { get; set; }

    public int storeId { get; set; }

    public int? folderId { get; set; }

    public string token { get; set; } = null!;

    public DateTime expiresAt { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(createdByUser))]
    public int createdByUserId { get; set; }

    public bool IsExpired => DateTime.UtcNow >= expiresAt;

    [ForeignKey(nameof(storeId))]
    public Store store { get; set; } = null!;

    [ForeignKey(nameof(folderId))]
    public StoreFolder? folder { get; set; }

    public User createdByUser { get; set; } = null!;
}
