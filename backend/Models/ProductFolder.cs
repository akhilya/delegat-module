using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

public class ProductFolder : IEntity, IMapToDTO<object>
{
    [Key]
    public int Id { get; set; }

    [StringLength(255)]
    public string name { get; set; } = null!;

    [ForeignKey(nameof(product))]
    public int productId { get; set; }

    [InverseProperty(nameof(Products.Product.folders))]
    public Products.Product product { get; set; } = null!;

    [ForeignKey(nameof(parentFolder))]
    public int? parentFolderId { get; set; }
    public ProductFolder? parentFolder { get; set; }

    [InverseProperty(nameof(parentFolder))]
    public virtual ICollection<ProductFolder> childFolders { get; set; } = new List<ProductFolder>();

    [InverseProperty(nameof(File.folder))]
    public virtual ICollection<File> files { get; set; } = new List<File>();

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public object MapToDTO(IServiceProvider sp) => new
    {
        Id,
        name,
        productId,
        parentFolderId,
        createdAt,
        fileCount = files?.Count ?? 0,
        subfolderCount = childFolders?.Count ?? 0
    };
}
