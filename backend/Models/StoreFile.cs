using PrintO.Enums;
using PrintO.Infrastructure.Interfaces;
using PrintO.Infrastructure.Storage;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

public class StoreFile : IEntity, IMapToDTO<object>
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(authorUser))]
    public int authorUserId { get; set; }
    public User authorUser { get; set; } = null!;

    [ForeignKey(nameof(store))]
    public int storeId { get; set; }
    public Store store { get; set; } = null!;

    public string filePath { get; set; } = null!;

    public string? displayName { get; set; }

    public DateTime uploadDateTime { get; set; }

    public FileType fileType { get; set; }

    public string contentType { get; set; } = null!;

    public long length { get; set; }

    [ForeignKey(nameof(folder))]
    public int? folderId { get; set; }
    public StoreFolder? folder { get; set; }

    public virtual ICollection<StoreFileTag> tags { get; set; } = new List<StoreFileTag>();

    public void Fill(string filePath, int authorUserId, int storeId, string contentType, long length)
    {
        this.filePath = filePath;
        this.authorUserId = authorUserId;
        this.storeId = storeId;
        this.contentType = contentType;
        this.length = length;
        uploadDateTime = DateTime.UtcNow;
    }

    public object MapToDTO(IServiceProvider serviceProvider)
    {
        return new
        {
            Id,
            uploadDateTime,
            fullPath = $"/api/store-storage/files/{Id}/download",
            fileName = displayName ?? Path.GetFileName(filePath),
            contentType,
            length,
            folderId,
            tags = tags.Select(t => new
            {
                id = t.tagDefinitionId,
                name = t.tagDefinition?.name ?? "",
                color = t.tagDefinition?.color ?? 0
            }),
        };
    }

    public string GetFullPath(IServiceProvider serviceProvider)
    {
        var storageService = serviceProvider.GetRequiredService<IStorageService>();
        return storageService.GetFullPath(filePath);
    }
}
