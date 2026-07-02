using Delegat.Enums;
using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Marketplace;

/// <summary>
/// Links a local ProductGroup to an external marketplace group
/// </summary>
public class ProductGroupMarketplaceLink : IEntity,
    IMapToDTO<ProductGroupMarketplaceLink.LinkDTO>,
    IAddable<ProductGroupMarketplaceLink.AddForm>,
    IUpdateable<ProductGroupMarketplaceLink.UpdateForm>
{
    /// <summary>
    /// Maximum length for external group identifier.
    /// </summary>
    public const int EXTERNAL_GROUP_ID_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for external group name.
    /// </summary>
    public const int EXTERNAL_GROUP_NAME_MAX_LENGTH = 500;

    /// <summary>
    /// Maximum length for error message.
    /// </summary>
    public const int ERROR_MESSAGE_MAX_LENGTH = 2000;

    /// <summary>
    /// Unique link identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the local product group.
    /// </summary>
    [ForeignKey(nameof(productGroup))]
    public int productGroupId { get; set; }

    /// <summary>
    /// Navigation property to the local product group.
    /// </summary>
    [InverseProperty(nameof(Models.ProductGroup.marketplaceLinks))]
    public ProductGroup productGroup { get; set; } = null!;

    /// <summary>
    /// Foreign key to the marketplace connection.
    /// </summary>
    [ForeignKey(nameof(connection))]
    public int connectionId { get; set; }

    /// <summary>
    /// Navigation property to the marketplace connection.
    /// </summary>
    [InverseProperty(nameof(MarketplaceConnection.productGroupLinks))]
    public MarketplaceConnection connection { get; set; } = null!;

    /// <summary>
    /// External group ID on the marketplace
    /// For Ozon: group_id field value
    /// For WB: nmID or similar group identifier
    /// </summary>
    [StringLength(EXTERNAL_GROUP_ID_MAX_LENGTH)]
    public string externalGroupId { get; set; } = null!;

    /// <summary>
    /// External group name (cached for display)
    /// </summary>
    [StringLength(EXTERNAL_GROUP_NAME_MAX_LENGTH)]
    public string? externalGroupName { get; set; }

    /// <summary>
    /// Sync status of this group link
    /// </summary>
    public ProductMarketplaceStatus status { get; set; } = ProductMarketplaceStatus.NotSynced;

    /// <summary>
    /// Error message if status is SyncError
    /// </summary>
    [StringLength(ERROR_MESSAGE_MAX_LENGTH)]
    public string? lastErrorMessage { get; set; }

    /// <summary>
    /// Timestamp of the last synchronization.
    /// </summary>
    public DateTime? lastSyncAt { get; set; }

    /// <summary>
    /// Timestamp when the link was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    #region IAddable / IUpdateable

    public bool AddFill(AddForm form)
    {
        productGroupId = form.productGroupId;
        connectionId = form.connectionId;
        externalGroupId = form.externalGroupId;
        externalGroupName = form.externalGroupName;
        status = form.status;
        createdAt = DateTime.UtcNow;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.externalGroupId))
            externalGroupId = form.externalGroupId;
        if (form.externalGroupName != null)
            externalGroupName = form.externalGroupName;
        if (form.status.HasValue)
            status = form.status.Value;
        if (form.lastErrorMessage != null)
            lastErrorMessage = form.lastErrorMessage;
        if (form.lastSyncAt.HasValue)
            lastSyncAt = form.lastSyncAt;
        return true;
    }

    #endregion

    #region IMapToDTO

    LinkDTO IMapToDTO<LinkDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new LinkDTO
        {
            id = Id,
            productGroupId = productGroupId,
            productGroupName = productGroup?.name,
            connectionId = connectionId,
            connectionName = connection?.name,
            adapterKey = connection?.adapterKey,
            externalGroupId = externalGroupId,
            externalGroupName = externalGroupName,
            status = status,
            lastErrorMessage = lastErrorMessage,
            lastSyncAt = lastSyncAt,
            createdAt = createdAt
        };
    }

    #endregion

    #region Forms & DTOs

    /// <summary>
    /// Form for creating a new product group marketplace link.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Local product group identifier.
        /// </summary>
        public int productGroupId { get; set; }

        /// <summary>
        /// Marketplace connection identifier.
        /// </summary>
        public int connectionId { get; set; }

        /// <summary>
        /// External group identifier on the marketplace.
        /// </summary>
        [StringLength(EXTERNAL_GROUP_ID_MAX_LENGTH)]
        public string externalGroupId { get; set; }

        /// <summary>
        /// External group name (cached for display).
        /// </summary>
        [StringLength(EXTERNAL_GROUP_NAME_MAX_LENGTH)]
        public string? externalGroupName { get; set; }

        /// <summary>
        /// Initial synchronization status.
        /// </summary>
        public ProductMarketplaceStatus status { get; set; }
    }

    /// <summary>
    /// Form for updating an existing product group marketplace link.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated external group identifier.
        /// </summary>
        [StringLength(EXTERNAL_GROUP_ID_MAX_LENGTH)]
        public string? externalGroupId { get; set; }

        /// <summary>
        /// Updated external group name.
        /// </summary>
        [StringLength(EXTERNAL_GROUP_NAME_MAX_LENGTH)]
        public string? externalGroupName { get; set; }

        /// <summary>
        /// Updated synchronization status.
        /// </summary>
        public ProductMarketplaceStatus? status { get; set; }

        /// <summary>
        /// Updated error message.
        /// </summary>
        [StringLength(ERROR_MESSAGE_MAX_LENGTH)]
        public string? lastErrorMessage { get; set; }

        /// <summary>
        /// Updated last sync timestamp.
        /// </summary>
        public DateTime? lastSyncAt { get; set; }
    }

    /// <summary>
    /// Product group marketplace link DTO for API responses.
    /// </summary>
    public struct LinkDTO
    {
        /// <summary>
        /// Unique link identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Local product group identifier.
        /// </summary>
        public int productGroupId { get; set; }

        /// <summary>
        /// Local product group name.
        /// </summary>
        public string? productGroupName { get; set; }

        /// <summary>
        /// Marketplace connection identifier.
        /// </summary>
        public int connectionId { get; set; }

        /// <summary>
        /// Marketplace connection display name.
        /// </summary>
        public string? connectionName { get; set; }

        /// <summary>
        /// Marketplace adapter key.
        /// </summary>
        public string? adapterKey { get; set; }

        /// <summary>
        /// External group identifier on the marketplace.
        /// </summary>
        public string externalGroupId { get; set; }

        /// <summary>
        /// External group name.
        /// </summary>
        public string? externalGroupName { get; set; }

        /// <summary>
        /// Current synchronization status.
        /// </summary>
        public ProductMarketplaceStatus status { get; set; }

        /// <summary>
        /// Error message if status is SyncError.
        /// </summary>
        public string? lastErrorMessage { get; set; }

        /// <summary>
        /// Timestamp of the last synchronization.
        /// </summary>
        public DateTime? lastSyncAt { get; set; }

        /// <summary>
        /// Timestamp when the link was created.
        /// </summary>
        public DateTime createdAt { get; set; }
    }

    #endregion
}
