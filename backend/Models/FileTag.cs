using PrintO.Enums;
using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

public class FileTag : IEntity, IMapToDTO<object>, IAddable<FileTag.AddForm>, IUpdateable<FileTag.UpdateForm>
{
    [Key]
    public int Id { get; set; }

    [StringLength(FILE_TAG_MAX_LENGTH)]
    public string tag { get; set; } = null!;
    public FileTagColor color { get; set; }

    [ForeignKey(nameof(file))]
    public int fileId { get; set; }
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

    public const int FILE_TAG_MAX_LENGTH = 50;

    public struct AddForm
    {
        public UserAddForm userAddForm { get; set; }
        public int fileId { get; set; }

        public AddForm(UserAddForm userAddForm, int fileId)
        {
            this.userAddForm = userAddForm;
            this.fileId = fileId;
        }
    }

    public struct UserAddForm
    {
        [StringLength(FILE_TAG_MAX_LENGTH)]
        public string tag { get; set; }
        public FileTagColor color { get; set; }
    }

    public struct UpdateForm
    {
        [StringLength(FILE_TAG_MAX_LENGTH)]
        public string? tag { get; set; }
        public FileTagColor? color { get; set; }
    }
}
