using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

public class InvitationToken : IEntity, IMapToDTO<object>, IAddable<InvitationToken.AddForm>, IUpdateable<InvitationToken.UseForm>
{
    [Key]
    public int Id { get; set; }

    [StringLength(64)]
    public string token { get; set; } = null!;

    public bool used { get; set; } = false;

    [ForeignKey(nameof(usedByUser))]
    public int? usedByUserId { get; set; }
    public User? usedByUser { get; set; }

    public DateTime createdAt { get; set; }
    public DateTime? usedAt { get; set; }

    public bool AddFill(AddForm form)
    {
        createdAt = DateTime.UtcNow;
        token = Guid.NewGuid().ToString();

        return true;
    }

    public bool UpdateFill(UseForm form)
    {
        if (used)
            return false;

        usedByUserId = form.usedByUserId;
        usedAt = DateTime.UtcNow;
        used = true;

        return true;
    }

    public object MapToDTO(IServiceProvider serviceProvider)
    {
        return new
        {
            token,
        };
    }

    /// <summary>
    /// DTO for listing tokens (includes more details for admin view)
    /// </summary>
    public object MapToListDTO(IServiceProvider serviceProvider)
    {
        return new
        {
            id = Id,
            token,
            used,
            createdAt,
            usedAt,
            usedByUserName = usedByUser?.UserName
        };
    }

    public struct AddForm
    {

    }

    public struct UseForm
    {
        public int usedByUserId { get; set; }

        public UseForm(int usedByUserId)
        {
            this.usedByUserId = usedByUserId;
        }
    }
}
