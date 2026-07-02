using Delegat.Infrastructure.Interfaces;
using Delegat.Models.Marketplace;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Local product group - represents a collection of related products
/// (e.g., same product in different variants/colors/sizes)
/// </summary>
public class ProductGroup : IEntity, IMapToDTO<ProductGroup.GroupDTO>,
    IMapToDTO<ProductGroup.GroupDetailDTO>,
    IAddable<ProductGroup.AddForm>, IUpdateable<ProductGroup.UpdateForm>
{
    /// <summary>
    /// Maximum length for group name.
    /// </summary>
    public const int GROUP_NAME_MAX_LENGTH = 200;

    /// <summary>
    /// Maximum length for group description.
    /// </summary>
    public const int GROUP_DESCRIPTION_MAX_LENGTH = 1000;

    /// <summary>
    /// Unique product group identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Group name for display purposes
    /// </summary>
    [StringLength(GROUP_NAME_MAX_LENGTH, MinimumLength = 1)]
    public string name { get; set; } = null!;

    /// <summary>
    /// Optional description
    /// </summary>
    [StringLength(GROUP_DESCRIPTION_MAX_LENGTH)]
    public string? description { get; set; }

    /// <summary>
    /// Store this group belongs to
    /// </summary>
    [ForeignKey(nameof(store))]
    public int storeId { get; set; }
    [InverseProperty(nameof(Store.productGroups))]
    public Store store { get; set; } = null!;

    /// <summary>
    /// Products in this group
    /// </summary>
    public virtual ICollection<ProductGroupMember> members { get; set; } = new List<ProductGroupMember>();

    /// <summary>
    /// Marketplace links for this group
    /// </summary>
    public virtual ICollection<ProductGroupMarketplaceLink> marketplaceLinks { get; set; } = new List<ProductGroupMarketplaceLink>();

    /// <summary>
    /// Timestamp when the group was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last update to the group.
    /// </summary>
    public DateTime? updatedAt { get; set; }

    #region IAddable / IUpdateable

    public bool AddFill(AddForm form)
    {
        name = form.name;
        description = form.description;
        storeId = form.storeId;
        createdAt = DateTime.UtcNow;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.name))
            name = form.name;
        if (form.description != null)
            description = form.description;
        updatedAt = DateTime.UtcNow;
        return true;
    }

    #endregion

    #region IMapToDTO

    GroupDTO IMapToDTO<GroupDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new GroupDTO
        {
            id = Id,
            name = name,
            description = description,
            storeId = storeId,
            membersCount = members?.Count ?? 0,
            marketplaceLinksCount = marketplaceLinks?.Count ?? 0,
            createdAt = createdAt
        };
    }

    GroupDetailDTO IMapToDTO<GroupDetailDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new GroupDetailDTO
        {
            id = Id,
            name = name,
            description = description,
            storeId = storeId,
            members = members?.OrderBy(m => m.sortOrder)
                .Select(m => new GroupMemberDTO
                {
                    id = m.Id,
                    product = new GroupProductDTO
                    {
                        id = m.productId,
                        sku = m.product?.sku,
                        name = m.product?.name,
                        category = m.product?.category != null ? new GroupProductCategoryDTO { name = m.product.category.name } : null,
                        images = m.product?.images?.OrderBy(i => i.index).Select(i => new GroupProductImageDTO
                        {
                            url = i.file?.GetDownloadUrl(),
                            thumbnailUrl = i.file != null && i.file.contentType.StartsWith("image/")
                                ? $"/api/files/{i.file.Id}/thumbnail?w=128"
                                : null
                        }).ToList() ?? new List<GroupProductImageDTO>()
                    },
                    sortOrder = m.sortOrder
                }).ToList() ?? new List<GroupMemberDTO>(),
            marketplaceLinks = marketplaceLinks?.Select(l =>
                ((IMapToDTO<ProductGroupMarketplaceLink.LinkDTO>)l).MapToDTO(serviceProvider)
            ).ToList() ?? new List<ProductGroupMarketplaceLink.LinkDTO>(),
            createdAt = createdAt,
            updatedAt = updatedAt
        };
    }

    #endregion

    #region Forms & DTOs

    /// <summary>
    /// Internal form used to create a new product group (includes store context).
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Group display name.
        /// </summary>
        [StringLength(GROUP_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Optional group description.
        /// </summary>
        [StringLength(GROUP_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Store identifier the group belongs to.
        /// </summary>
        public int storeId { get; set; }
    }

    /// <summary>
    /// User-facing form for creating a new product group.
    /// </summary>
    public struct UserAddForm
    {
        /// <summary>
        /// Group display name.
        /// </summary>
        [StringLength(GROUP_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Optional group description.
        /// </summary>
        [StringLength(GROUP_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Product identifiers to add as initial members.
        /// </summary>
        public List<int>? productIds { get; set; }
    }

    /// <summary>
    /// Form for updating an existing product group.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated group name.
        /// </summary>
        [StringLength(GROUP_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string? name { get; set; }

        /// <summary>
        /// Updated group description.
        /// </summary>
        [StringLength(GROUP_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }
    }

    /// <summary>
    /// Product group summary DTO for list views.
    /// </summary>
    public struct GroupDTO
    {
        /// <summary>
        /// Unique group identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Group display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Optional group description.
        /// </summary>
        public string? description { get; set; }

        /// <summary>
        /// Store identifier.
        /// </summary>
        public int storeId { get; set; }

        /// <summary>
        /// Number of products in the group.
        /// </summary>
        public int membersCount { get; set; }

        /// <summary>
        /// Number of marketplace links for the group.
        /// </summary>
        public int marketplaceLinksCount { get; set; }

        /// <summary>
        /// Timestamp when the group was created.
        /// </summary>
        public DateTime createdAt { get; set; }
    }

    /// <summary>
    /// Detailed product group DTO including members and marketplace links.
    /// </summary>
    public struct GroupDetailDTO
    {
        /// <summary>
        /// Unique group identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Group display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Optional group description.
        /// </summary>
        public string? description { get; set; }

        /// <summary>
        /// Store identifier.
        /// </summary>
        public int storeId { get; set; }

        /// <summary>
        /// Ordered list of group members.
        /// </summary>
        public List<GroupMemberDTO> members { get; set; }

        /// <summary>
        /// Marketplace links for the group.
        /// </summary>
        public List<ProductGroupMarketplaceLink.LinkDTO> marketplaceLinks { get; set; }

        /// <summary>
        /// Timestamp when the group was created.
        /// </summary>
        public DateTime createdAt { get; set; }

        /// <summary>
        /// Timestamp of the last update.
        /// </summary>
        public DateTime? updatedAt { get; set; }
    }

    /// <summary>
    /// DTO representing a product member within a group.
    /// </summary>
    public struct GroupMemberDTO
    {
        /// <summary>
        /// Member record identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Product data for the member.
        /// </summary>
        public GroupProductDTO product { get; set; }

        /// <summary>
        /// Display order within the group.
        /// </summary>
        public int sortOrder { get; set; }
    }

    /// <summary>
    /// Lightweight product DTO used within group context.
    /// </summary>
    public struct GroupProductDTO
    {
        /// <summary>
        /// Product identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Product SKU.
        /// </summary>
        public string? sku { get; set; }

        /// <summary>
        /// Product display name.
        /// </summary>
        public string? name { get; set; }

        /// <summary>
        /// Product category info.
        /// </summary>
        public GroupProductCategoryDTO? category { get; set; }

        /// <summary>
        /// Ordered list of product images.
        /// </summary>
        public List<GroupProductImageDTO> images { get; set; }
    }

    /// <summary>
    /// Minimal category DTO used within group product context.
    /// </summary>
    public struct GroupProductCategoryDTO
    {
        /// <summary>
        /// Category display name.
        /// </summary>
        public string name { get; set; }
    }

    /// <summary>
    /// Product image DTO used within group context.
    /// </summary>
    public struct GroupProductImageDTO
    {
        /// <summary>
        /// Full URL of the image.
        /// </summary>
        public string? url { get; set; }

        /// <summary>
        /// Thumbnail URL of the image (smaller, faster to load).
        /// </summary>
        public string? thumbnailUrl { get; set; }
    }

    #endregion
}
