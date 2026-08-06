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

    /// <summary>
    /// Username for display and authentication.
    /// </summary>
    [StringLength(USER_NAME_MAX_LENGTH)]
    public override string? UserName { get => base.UserName; set => base.UserName = value; }

    /// <summary>
    /// Whether the user is the instance owner marker (break-glass / can assign isAdmin).
    /// API authorization uses permission claims, not this flag alone.
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
            isActive = isActive,
            aiEnabled = Delegat.Infrastructure.Configuration.FeatureFlags.IsAiEnabled(),
            pluginsEnabled = Delegat.Infrastructure.Configuration.FeatureFlags.IsPluginsEnabled(),
            roles = Array.Empty<string>(),
            permissions = Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Maximum length for username.
    /// </summary>
    public const int USER_NAME_MAX_LENGTH = 100;

    /// <summary>
    /// User data transfer object for API responses.
    /// </summary>
    public class UserDTO
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
        /// Instance owner marker (not used alone for API authorization).
        /// </summary>
        public bool isAdmin { get; set; }

        /// <summary>
        /// Whether the user can sign in and use the dashboard.
        /// </summary>
        public bool isActive { get; set; }

        /// <summary>
        /// Whether the AI assistant is enabled for this instance (from AI_ENABLED env).
        /// </summary>
        public bool aiEnabled { get; set; }

        /// <summary>
        /// Whether the Plugins admin page (import/reload/delete) is enabled (PLUGINS_ENABLED).
        /// System marketplace adapters load regardless.
        /// </summary>
        public bool pluginsEnabled { get; set; }

        /// <summary>Whether error logs are persisted (admin toggle).</summary>
        public bool logErrorsEnabled { get; set; }

        /// <summary>Whether Public API request logs are persisted (admin toggle).</summary>
        public bool logApiRequestsEnabled { get; set; }

        /// <summary>Whether adapter HTTP logs are persisted (admin toggle).</summary>
        public bool logAdapterHttpEnabled { get; set; }

        /// <summary>Whether Public API (/api/v1) accepts traffic (admin toggle).</summary>
        public bool publicApiEnabled { get; set; } = true;

        /// <summary>Assigned Identity role names.</summary>
        public IReadOnlyList<string> roles { get; set; } = Array.Empty<string>();

        /// <summary>Effective permission claims.</summary>
        public IReadOnlyList<string> permissions { get; set; } = Array.Empty<string>();
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
