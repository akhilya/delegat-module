using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Represents a reference linking a file to a product as an ordered image.
/// </summary>
public class ImageReference : IEntity, IMapToDTO<object>, IAddable<ImageReference.AddForm>, IUpdateable<ImageReference.UpdateForm>
{
    /// <summary>
    /// Unique image reference identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the underlying file.
    /// </summary>
    [ForeignKey(nameof(file))]
    public int fileId { get; set; }

    /// <summary>
    /// Navigation property to the underlying file.
    /// </summary>
    public File file { get; set; } = null!;

    /// <summary>
    /// Foreign key to the associated product.
    /// </summary>
    [ForeignKey(nameof(product))]
    public int productId { get; set; }

    /// <summary>
    /// Navigation property to the associated product.
    /// </summary>
    [InverseProperty(nameof(Products.Product.images))]
    public Products.Product product { get; set; } = null!;

    /// <summary>
    /// Display order index (lower values appear first).
    /// </summary>
    public int index { get; set; }

    public bool AddFill(AddForm form)
    {
        fileId = form.fileId;
        index = form.index;
        productId = form.productId;

        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        index = form.index;
        fileId = form.fileId;

        return true;
    }

    public object MapToDTO(IServiceProvider serviceProvider)
    {
        return new
        {
            Id,
            fileId,
            index,
        };
    }

    /// <summary>
    /// Form for creating a new image reference.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// File identifier of the image.
        /// </summary>
        public int fileId { get; set; }

        /// <summary>
        /// Product identifier to associate with.
        /// </summary>
        public int productId { get; set; }

        /// <summary>
        /// Display order index.
        /// </summary>
        public int index { get; set; }

        public AddForm(int fileId, int productId, int index)
        {
            this.fileId = fileId;
            this.productId = productId;
            this.index = index;
        }
    }

    /// <summary>
    /// Form for updating an existing image reference.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated display order index.
        /// </summary>
        public int index { get; set; }

        /// <summary>
        /// Updated file identifier.
        /// </summary>
        public int fileId { get; set; }

        public UpdateForm(int index, int fileId)
        {
            this.index = index;
            this.fileId = fileId;
        }
    }
}
