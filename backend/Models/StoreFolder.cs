using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

public class StoreFolder : IEntity, IMapToDTO<object>
{
    [Key]
    public int Id { get; set; }

    [StringLength(255)]
    public string name { get; set; } = null!;

    [ForeignKey(nameof(store))]
    public int storeId { get; set; }
    public Store store { get; set; } = null!;

    [ForeignKey(nameof(parentFolder))]
    public int? parentFolderId { get; set; }
    public StoreFolder? parentFolder { get; set; }

    [InverseProperty(nameof(parentFolder))]
    public virtual ICollection<StoreFolder> childFolders { get; set; } = new List<StoreFolder>();

    [InverseProperty(nameof(StoreFile.folder))]
    public virtual ICollection<StoreFile> files { get; set; } = new List<StoreFile>();

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public object MapToDTO(IServiceProvider sp) => new
    {
        Id,
        name,
        storeId,
        parentFolderId,
        createdAt,
        fileCount = files?.Count ?? 0,
        subfolderCount = childFolders?.Count ?? 0
    };
}
