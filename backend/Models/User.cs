using Microsoft.AspNetCore.Identity;
using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Represents an application user, extending ASP.NET Identity.
/// </summary>
public class User : IdentityUser<int>, IEntity, IMapToDTO<User.UserDTO>, IUpdateable<User.SelectStoreForm>
{
    int IEntity.Id
    {
        get => Id;
        set => Id = value;
    }

    /// <summary>
    /// Foreign key to the currently selected store.
    /// </summary>
    public int? selectedStoreId { get; set; }

    /// <summary>
    /// Navigation property to the currently selected store.
    /// </summary>
    public Store? selectedStore { get; set; }

    /// <summary>
    /// Collection of stores the user is a member of.
    /// </summary>
    [InverseProperty(nameof(Store.members))]
    public virtual ICollection<Store> memberships { get; set; } = new List<Store>();

    [InverseProperty(nameof(Store.storeAdmins))]
    public virtual ICollection<Store> storeAdminMemberships { get; set; } = new List<Store>();

    /// <summary>
    /// Username for display and authentication.
    /// </summary>
    [StringLength(USER_NAME_MAX_LENGTH)]
    public override string? UserName { get => base.UserName; set => base.UserName = value; }

    /// <summary>
    /// Whether the user has administrator privileges.
    /// </summary>
    public bool isAdmin { get; set; }

    /// <summary>
    /// Whether the user can sign in and use the dashboard.
    /// </summary>
    public bool isActive { get; set; } = true;

    public bool UpdateFill(SelectStoreForm form)
    {
        selectedStoreId = form.storeId;
        return true;
    }

    public UserDTO MapToDTO(IServiceProvider serviceProvider)
    {
        return new UserDTO
        {
            id = Id,
            userName = UserName,
            selectedStoreId = selectedStoreId,
            isAdmin = isAdmin,
            isActive = isActive
        };
    }

    /// <summary>
    /// Maximum length for username.
    /// </summary>
    public const int USER_NAME_MAX_LENGTH = 100;

    /// <summary>
    /// User data transfer object for API responses.
    /// </summary>
    public struct UserDTO
    {
        /// <summary>
        /// Unique user identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Username for display.
        /// </summary>
        public string? userName { get; set; }

        /// <summary>
        /// Currently selected store identifier.
        /// </summary>
        public int? selectedStoreId { get; set; }

        /// <summary>
        /// Whether the user has administrator privileges.
        /// </summary>
        public bool isAdmin { get; set; }

        /// <summary>
        /// Whether the user can sign in and use the dashboard.
        /// </summary>
        public bool isActive { get; set; }
    }

    /// <summary>
    /// Form for selecting the active store for a user.
    /// </summary>
    public struct SelectStoreForm
    {
        /// <summary>
        /// Store identifier to select.
        /// </summary>
        public int storeId { get; set; }

        public SelectStoreForm(int storeId)
        {
            this.storeId = storeId;
        }
    }
}
