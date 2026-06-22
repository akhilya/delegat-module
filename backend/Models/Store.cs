using PrintO.Infrastructure.Interfaces;
using PrintO.Models.Marketplace;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

/// <summary>
/// Represents a store (workspace) that contains products, categories, and marketplace connections.
/// </summary>
public class Store : IEntity, IMapToDTO<Store.StoreDTO>, IAddable<Store.AddForm>, IUpdateable<Store.UserJoinForm>
{
    /// <summary>
    /// Unique store identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Store display name.
    /// </summary>
    [StringLength(STORE_NAME_MAX_LENGTH)]
    public string name { get; set; } = null!;

    /// <summary>
    /// Collection of users who are members of this store.
    /// </summary>
    [InverseProperty(nameof(User.memberships))]
    public virtual ICollection<User> members { get; set; } = new List<User>();

    [InverseProperty(nameof(User.storeAdminMemberships))]
    public virtual ICollection<User> storeAdmins { get; set; } = new List<User>();

    /// <summary>
    /// Collection of products belonging to this store.
    /// </summary>
    public virtual ICollection<Products.Product> products { get; set; } = new List<Products.Product>();

    /// <summary>
    /// Collection of categories defined in this store.
    /// </summary>
    public virtual ICollection<Category> categories { get; set; } = new List<Category>();

    /// <summary>
    /// Collection of store-level custom attributes.
    /// </summary>
    public virtual ICollection<StoreAttribute> storeAttributes { get; set; } = new List<StoreAttribute>();

    /// <summary>
    /// Collection of marketplace connections for this store.
    /// </summary>
    public virtual ICollection<MarketplaceConnection> marketplaceConnections { get; set; } = new List<MarketplaceConnection>();

    /// <summary>
    /// Collection of product groups in this store.
    /// </summary>
    public virtual ICollection<ProductGroup> productGroups { get; set; } = new List<ProductGroup>();

    public bool AddFill(AddForm form)
    {
        name = form.name;
        return true;
    }

    public bool UpdateFill(UserJoinForm form)
    {
        members.Add(form.user);
        return true;
    }

    public StoreDTO MapToDTO(IServiceProvider serviceProvider)
    {
        return new StoreDTO
        {
            id = Id,
            name = name,
            membersCount = members?.Count(m => m.isActive) ?? 0,
            storeAdminsCount = storeAdmins?.Count(m => m.isActive) ?? 0,
        };
    }

    public StoreDetailDTO MapToDetailDTO(IServiceProvider serviceProvider)
    {
        return new StoreDetailDTO
        {
            id = Id,
            name = name,
            members = members?.Where(m => m.isActive).Select(m => new MemberDTO
            {
                id = m.Id,
                userName = m.UserName ?? "",
                isStoreAdmin = storeAdmins?.Any(a => a.Id == m.Id) == true
            }).ToList() ?? new List<MemberDTO>()
        };
    }

    /// <summary>
    /// Maximum length for store name.
    /// </summary>
    public const int STORE_NAME_MAX_LENGTH = 40;

    /// <summary>
    /// Form for creating a new store.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Store display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Add every currently active user as a member of the new store.
        /// </summary>
        public bool addAllUsers { get; set; }
    }

    /// <summary>
    /// Form for adding a user to a store.
    /// </summary>
    public struct UserJoinForm
    {
        /// <summary>
        /// User to add to the store.
        /// </summary>
        public User user { get; set; }

        public UserJoinForm(User user)
        {
            this.user = user;
        }
    }

    /// <summary>
    /// Store summary DTO for list views.
    /// </summary>
    public struct StoreDTO
    {
        /// <summary>
        /// Unique store identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Store display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Number of members in the store.
        /// </summary>
        public int membersCount { get; set; }

        public int storeAdminsCount { get; set; }

        public bool isStoreAdmin { get; set; }
    }

    /// <summary>
    /// Detailed store DTO including member list.
    /// </summary>
    public struct StoreDetailDTO
    {
        /// <summary>
        /// Unique store identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Store display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// List of store members.
        /// </summary>
        public List<MemberDTO> members { get; set; }
    }

    /// <summary>
    /// Store member data transfer object.
    /// </summary>
    public struct MemberDTO
    {
        /// <summary>
        /// User identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Username of the member.
        /// </summary>
        public string userName { get; set; }

        public bool isStoreAdmin { get; set; }
    }
}
