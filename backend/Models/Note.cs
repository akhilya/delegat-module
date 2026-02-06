using PrintO.Infrastructure.Interfaces;
using PrintO.Models.Products;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

/// <summary>
/// Represents a text note attached to a product with an importance level.
/// </summary>
public class Note : IEntity, IMapToDTO<object>, IAddable<Note.AddForm>, IUpdateable<Note.UpdateForm>
{
    /// <summary>
    /// Unique note identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Note text content.
    /// </summary>
    [StringLength(NOTE_TEXT_MAX_LENGTH)]
    public string text { get; set; } = null!;

    /// <summary>
    /// Importance level of the note.
    /// </summary>
    public Importance importance { get; set; }

    /// <summary>
    /// Foreign key to the associated product.
    /// </summary>
    [ForeignKey(nameof(product))]
    public int productId { get; set; }

    /// <summary>
    /// Navigation property to the associated product.
    /// </summary>
    [InverseProperty(nameof(Product.notes))]
    public Product product { get; set; } = null!;

    public bool AddFill(AddForm form)
    {
        text = form.userAddForm.text;
        importance = form.userAddForm.importance;
        productId = form.productId;

        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.text))
            text = form.text;
        if (form.importance.HasValue)
            importance = form.importance.Value;

        return true;
    }

    public object MapToDTO(IServiceProvider serviceProvider)
    {
        return new
        {
            Id,
            text,
            importance
        };
    }

    /// <summary>
    /// Maximum length for note text.
    /// </summary>
    public const int NOTE_TEXT_MAX_LENGTH = 10000;

    /// <summary>
    /// Internal form used to create a new note (includes product context).
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// User-provided note creation data.
        /// </summary>
        public UserAddForm userAddForm { get; set; }

        /// <summary>
        /// Product identifier to attach the note to.
        /// </summary>
        public int productId { get; set; }

        public AddForm(UserAddForm userAddForm, int productId)
        {
            this.userAddForm = userAddForm;
            this.productId = productId;
        }
    }

    /// <summary>
    /// User-facing form for creating a new note.
    /// </summary>
    public struct UserAddForm
    {
        /// <summary>
        /// Note text content.
        /// </summary>
        [StringLength(NOTE_TEXT_MAX_LENGTH)]
        public string text { get; set; }

        /// <summary>
        /// Importance level of the note.
        /// </summary>
        public Importance importance { get; set; }
    }

    /// <summary>
    /// Form for updating an existing note.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated note text content.
        /// </summary>
        [StringLength(NOTE_TEXT_MAX_LENGTH)]
        public string? text { get; set; }

        /// <summary>
        /// Updated importance level.
        /// </summary>
        public Importance? importance { get; set; }
    }
}
