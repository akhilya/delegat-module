using PrintO.Enums;
using PrintO.Infrastructure.Interfaces;
using PrintO.Infrastructure.Storage;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

public class File : IEntity, IMapToDTO<object>, IAddable<File.AddForm>
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(authorUser))]
    public int authorUserId { get; set; }
    public User authorUser { get; set; } = null!;

    [ForeignKey(nameof(product))]
    public int productId { get; set; }
    [InverseProperty(nameof(Products.Product.files))]
    public Products.Product product { get; set; } = null!;

    public string filePath { get; set; } = null!;
    public DateTime uploadDateTime { get; set; }
    public FileType fileType { get; set; }
    public string contentType { get; set; } = null!;
    public long length { get; set; }

    public virtual ICollection<FileTag> tags { get; set; } = new List<FileTag>();

    public bool AddFill(AddForm form)
    {
        filePath = form.filePath;
        authorUserId = form.authorUserId;
        uploadDateTime = DateTime.UtcNow;
        productId = form.productId;
        contentType = form.contentType;
        length = form.length;

        return true;
    }

    public object MapToDTO(IServiceProvider serviceProvider)
    {
        var storageService = serviceProvider.GetRequiredService<IStorageService>();
        string fullPath = storageService.GetFullPath(filePath);

        return new
        {
            Id,
            uploadDateTime,
            fullPath,
            contentType,
            length,
            tags = tags.Select(t => t.MapToDTO(serviceProvider)),
        };
    }

    public string GetFullPath(IServiceProvider serviceProvider)
    {
        var storageService = serviceProvider.GetRequiredService<IStorageService>();
        return storageService.GetFullPath(filePath);
    }

    public struct AddForm
    {
        public string filePath { get; set; }
        public int authorUserId { get; set; }
        public int productId { get; set; }
        public string contentType { get; set; }
        public long length { get; set; }

        public AddForm(string filePath, int authorUserId, int productId, string contentType, long length)
        {
            this.filePath = filePath;
            this.authorUserId = authorUserId;
            this.productId = productId;
            this.contentType = contentType;
            this.length = length;
        }
    }
}
