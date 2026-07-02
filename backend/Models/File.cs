using Delegat.Enums;
using Delegat.Infrastructure.Interfaces;
using Delegat.Infrastructure.Storage;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Represents an uploaded file stored in object storage (MinIO).
/// </summary>
public class File : IEntity, IMapToDTO<object>, IAddable<File.AddForm>
{
    /// <summary>
    /// Unique file identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the user who uploaded the file.
    /// </summary>
    [ForeignKey(nameof(authorUser))]
    public int authorUserId { get; set; }

    /// <summary>
    /// Navigation property to the uploading user.
    /// </summary>
    public User authorUser { get; set; } = null!;

    /// <summary>
    /// Foreign key to the associated product.
    /// </summary>
    [ForeignKey(nameof(product))]
    public int productId { get; set; }

    /// <summary>
    /// Navigation property to the associated product.
    /// </summary>
    [InverseProperty(nameof(Products.Product.files))]
    public Products.Product product { get; set; } = null!;

    /// <summary>
    /// Relative storage path in object storage (the actual MinIO object key — never changes after upload).
    /// </summary>
    public string filePath { get; set; } = null!;

    /// <summary>
    /// User-facing display name. Set when the user renames a file.
    /// Null means use the filename from filePath.
    /// </summary>
    public string? displayName { get; set; }

    /// <summary>
    /// Timestamp when the file was uploaded.
    /// </summary>
    public DateTime uploadDateTime { get; set; }

    /// <summary>
    /// Type classification of the file (image, document, etc.).
    /// </summary>
    public FileType fileType { get; set; }

    /// <summary>
    /// MIME content type of the file.
    /// </summary>
    public string contentType { get; set; } = null!;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long length { get; set; }

    /// <summary>
    /// When true the file is served at a permanent public URL with no authentication.
    /// </summary>
    public bool isPublic { get; set; } = false;

    /// <summary>
    /// Optional folder this file belongs to (null = root level).
    /// </summary>
    [ForeignKey(nameof(folder))]
    public int? folderId { get; set; }

    /// <summary>
    /// Navigation property to the containing folder.
    /// </summary>
    public ProductFolder? folder { get; set; }

    /// <summary>
    /// Collection of tags associated with this file.
    /// </summary>
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

    private static string ExtensionFromContentType(string? ct) => ct switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "application/pdf" => ".pdf",
        "video/mp4" => ".mp4",
        "video/webm" => ".webm",
        "application/zip" => ".zip",
        _ => ""
    };

    public object MapToDTO(IServiceProvider serviceProvider)
    {
        var rawName = displayName ?? Path.GetFileName(filePath);
        string fileName;
        if (Path.HasExtension(rawName))
        {
            fileName = rawName;
        }
        else
        {
            var ext = ExtensionFromContentType(contentType);
            if (string.IsNullOrEmpty(ext))
                ext = Path.GetExtension(filePath);
            fileName = rawName + ext;
        }
        return new
        {
            Id,
            uploadDateTime,
            fullPath = $"/api/files/{Id}/download",
            publicUrl = isPublic ? $"/api/pub/files/{Id}/{Uri.EscapeDataString(fileName)}" : (string?)null,
            isPublic,
            fileName,
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

    public string GetDownloadUrl() => $"/api/files/{Id}/download";

    public string GetFullPath(IServiceProvider serviceProvider)
    {
        var storageService = serviceProvider.GetRequiredService<IStorageService>();
        return storageService.GetFullPath(filePath);
    }

    /// <summary>
    /// Form for creating a new file record.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Relative storage path in object storage.
        /// </summary>
        public string filePath { get; set; }

        /// <summary>
        /// Identifier of the uploading user.
        /// </summary>
        public int authorUserId { get; set; }

        /// <summary>
        /// Identifier of the associated product.
        /// </summary>
        public int productId { get; set; }

        /// <summary>
        /// MIME content type of the file.
        /// </summary>
        public string contentType { get; set; }

        /// <summary>
        /// File size in bytes.
        /// </summary>
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
