using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

/// <summary>
/// Represents a single-use invitation token for user registration.
/// </summary>
public class InvitationToken : IEntity, IMapToDTO<object>, IAddable<InvitationToken.AddForm>, IUpdateable<InvitationToken.UseForm>
{
    /// <summary>
    /// Unique token record identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Unique invitation token string (GUID).
    /// </summary>
    [StringLength(64)]
    public string token { get; set; } = null!;

    /// <summary>
    /// Whether the token has been used.
    /// </summary>
    public bool used { get; set; } = false;

    /// <summary>
    /// Foreign key to the user who redeemed the token.
    /// </summary>
    [ForeignKey(nameof(usedByUser))]
    public int? usedByUserId { get; set; }

    /// <summary>
    /// Navigation property to the user who redeemed the token.
    /// </summary>
    public User? usedByUser { get; set; }

    /// <summary>
    /// Timestamp when the token was created.
    /// </summary>
    public DateTime createdAt { get; set; }

    /// <summary>
    /// Timestamp when the token was used.
    /// </summary>
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

    /// <summary>
    /// Empty form for creating a new invitation token (token is auto-generated).
    /// </summary>
    public struct AddForm
    {

    }

    /// <summary>
    /// Form for redeeming an invitation token.
    /// </summary>
    public struct UseForm
    {
        /// <summary>
        /// Identifier of the user redeeming the token.
        /// </summary>
        public int usedByUserId { get; set; }

        public UseForm(int usedByUserId)
        {
            this.usedByUserId = usedByUserId;
        }
    }
}
