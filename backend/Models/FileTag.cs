using PrintO.Enums;
using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

/// <summary>
/// Represents a colored tag attached to a file for organization purposes.
/// </summary>
public class FileTag : IEntity, IMapToDTO<object>, IAddable<FileTag.AddForm>, IUpdateable<FileTag.UpdateForm>
{
    /// <summary>
    /// Unique tag identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Tag label text.
    /// </summary>
    [StringLength(FILE_TAG_MAX_LENGTH)]
    public string tag { get; set; } = null!;

    /// <summary>
    /// Display color for the tag.
    /// </summary>
    public FileTagColor color { get; set; }

    /// <summary>
    /// Foreign key to the tagged file.
    /// </summary>
    [ForeignKey(nameof(file))]
    public int fileId { get; set; }

    /// <summary>
    /// Navigation property to the tagged file.
    /// </summary>
    [InverseProperty(nameof(File.tags))]
    public File file { get; set; } = null!;

    public bool AddFill(AddForm form)
    {
        tag = form.userAddForm.tag;
        color = form.userAddForm.color;
        fileId = form.fileId;

        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.tag))
            tag = form.tag;
        if (form.color.HasValue)
            color = form.color.Value;

        return true;
    }

    public object MapToDTO(IServiceProvider serviceProvider)
    {
        return new
        {
            Id,
            tag,
            color
        };
    }

    /// <summary>
    /// Maximum length for tag text.
    /// </summary>
    public const int FILE_TAG_MAX_LENGTH = 50;

    /// <summary>
    /// Internal form used to create a new file tag (includes file context).
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// User-provided tag creation data.
        /// </summary>
        public UserAddForm userAddForm { get; set; }

        /// <summary>
        /// Identifier of the file to tag.
        /// </summary>
        public int fileId { get; set; }

        public AddForm(UserAddForm userAddForm, int fileId)
        {
            this.userAddForm = userAddForm;
            this.fileId = fileId;
        }
    }

    /// <summary>
    /// User-facing form for creating a new file tag.
    /// </summary>
    public struct UserAddForm
    {
        /// <summary>
        /// Tag label text.
        /// </summary>
        [StringLength(FILE_TAG_MAX_LENGTH)]
        public string tag { get; set; }

        /// <summary>
        /// Display color for the tag.
        /// </summary>
        public FileTagColor color { get; set; }
    }

    /// <summary>
    /// Form for updating an existing file tag.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated tag label text.
        /// </summary>
        [StringLength(FILE_TAG_MAX_LENGTH)]
        public string? tag { get; set; }

        /// <summary>
        /// Updated display color.
        /// </summary>
        public FileTagColor? color { get; set; }
    }
}
