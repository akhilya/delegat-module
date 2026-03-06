using PrintO.Infrastructure.Interfaces;
using PrintO.Infrastructure.Security;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models.Marketplace;

/// <summary>
/// Represents a connection to an external marketplace (e.g., Ozon, Wildberries).
/// </summary>
public class MarketplaceConnection : IEntity, IMapToDTO<MarketplaceConnection.ConnectionDTO>,
    IMapToDTO<MarketplaceConnection.ConnectionDetailDTO>,
    IAddable<MarketplaceConnection.AddForm>, IUpdateable<MarketplaceConnection.UpdateForm>
{
    /// <summary>
    /// Unique connection identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Connection display name.
    /// </summary>
    [StringLength(NAME_MAX_LENGTH, MinimumLength = 1)]
    public string name { get; set; } = null!;

    /// <summary>
    /// Marketplace adapter key (e.g., "wildberries", "ozon").
    /// </summary>
    [StringLength(ADAPTER_KEY_MAX_LENGTH, MinimumLength = 1)]
    public string adapterKey { get; set; } = null!;

    /// <summary>
    /// JSON-serialized credentials (structure depends on the adapter).
    /// </summary>
    [StringLength(CREDENTIALS_MAX_LENGTH)]
    public string? credentialsJson { get; set; }

    /// <summary>
    /// JSON-serialized additional settings.
    /// </summary>
    [StringLength(SETTINGS_MAX_LENGTH)]
    public string? settingsJson { get; set; }

    /// <summary>
    /// Whether the connection is currently active.
    /// </summary>
    public bool isActive { get; set; } = true;

    /// <summary>
    /// Timestamp of the last synchronization.
    /// </summary>
    public DateTime? lastSyncAt { get; set; }

    /// <summary>
    /// Timestamp when the connection was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Foreign key to the owning store.
    /// </summary>
    [ForeignKey(nameof(store))]
    public int storeId { get; set; }

    /// <summary>
    /// Navigation property to the owning store.
    /// </summary>
    [InverseProperty(nameof(Models.Store.marketplaceConnections))]
    public Store store { get; set; } = null!;

    /// <summary>
    /// Collection of category mappings for this connection.
    /// </summary>
    public virtual ICollection<CategoryMapping> categoryMappings { get; set; } = new List<CategoryMapping>();

    /// <summary>
    /// Collection of product marketplace links for this connection.
    /// </summary>
    public virtual ICollection<ProductMarketplaceLink> productLinks { get; set; } = new List<ProductMarketplaceLink>();

    /// <summary>
    /// Collection of product group marketplace links for this connection.
    /// </summary>
    public virtual ICollection<ProductGroupMarketplaceLink> productGroupLinks { get; set; } = new List<ProductGroupMarketplaceLink>();

    #region IAddable / IUpdateable

    public bool AddFill(AddForm form)
    {
        name = form.name;
        adapterKey = form.adapterKey;
        credentialsJson = form.credentialsJson;
        settingsJson = form.settingsJson;
        isActive = form.isActive;
        storeId = form.storeId;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.name))
            name = form.name;
        if (form.credentialsJson != null)
            credentialsJson = form.credentialsJson;
        if (form.settingsJson != null)
            settingsJson = form.settingsJson;
        if (form.isActive.HasValue)
            isActive = form.isActive.Value;
        return true;
    }

    #endregion

    #region IMapToDTO

    ConnectionDTO IMapToDTO<ConnectionDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new ConnectionDTO
        {
            id = Id,
            name = name,
            adapterKey = adapterKey,
            isActive = isActive,
            lastSyncAt = lastSyncAt,
            categoryMappingsCount = categoryMappings?.Count ?? 0,
            productLinksCount = productLinks?.Count ?? 0
        };
    }

    ConnectionDetailDTO IMapToDTO<ConnectionDetailDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        var decryptedCreds = credentialsJson;
        var encryption = serviceProvider.GetService(typeof(EncryptionService)) as EncryptionService;
        if (encryption != null && !string.IsNullOrEmpty(credentialsJson) && encryption.IsEncrypted(credentialsJson))
            decryptedCreds = encryption.Decrypt(credentialsJson);

        return new ConnectionDetailDTO
        {
            id = Id,
            name = name,
            adapterKey = adapterKey,
            credentialsJson = decryptedCreds,
            settingsJson = settingsJson,
            isActive = isActive,
            lastSyncAt = lastSyncAt,
            createdAt = createdAt,
            categoryMappingsCount = categoryMappings?.Count ?? 0,
            productLinksCount = productLinks?.Count ?? 0
        };
    }

    #endregion

    #region Constants

    /// <summary>
    /// Maximum length for connection name.
    /// </summary>
    public const int NAME_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for adapter key.
    /// </summary>
    public const int ADAPTER_KEY_MAX_LENGTH = 50;

    /// <summary>
    /// Maximum length for credentials JSON.
    /// </summary>
    public const int CREDENTIALS_MAX_LENGTH = 5000;

    /// <summary>
    /// Maximum length for settings JSON.
    /// </summary>
    public const int SETTINGS_MAX_LENGTH = 10000;

    #endregion

    #region Forms & DTOs

    /// <summary>
    /// Internal form used to create a new marketplace connection (includes store context).
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Connection display name.
        /// </summary>
        [StringLength(NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Marketplace adapter key.
        /// </summary>
        [StringLength(ADAPTER_KEY_MAX_LENGTH, MinimumLength = 1)]
        public string adapterKey { get; set; }

        /// <summary>
        /// JSON-serialized credentials.
        /// </summary>
        [StringLength(CREDENTIALS_MAX_LENGTH)]
        public string? credentialsJson { get; set; }

        /// <summary>
        /// JSON-serialized settings.
        /// </summary>
        [StringLength(SETTINGS_MAX_LENGTH)]
        public string? settingsJson { get; set; }

        /// <summary>
        /// Whether the connection is active.
        /// </summary>
        public bool isActive { get; set; }

        /// <summary>
        /// Store identifier.
        /// </summary>
        public int storeId { get; set; }
    }

    /// <summary>
    /// User-facing form for creating a new marketplace connection.
    /// </summary>
    public struct UserAddForm
    {
        /// <summary>
        /// Connection display name.
        /// </summary>
        [StringLength(NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Marketplace adapter key.
        /// </summary>
        [StringLength(ADAPTER_KEY_MAX_LENGTH, MinimumLength = 1)]
        public string adapterKey { get; set; }

        /// <summary>
        /// JSON-serialized credentials.
        /// </summary>
        [StringLength(CREDENTIALS_MAX_LENGTH)]
        public string? credentialsJson { get; set; }

        /// <summary>
        /// JSON-serialized settings.
        /// </summary>
        [StringLength(SETTINGS_MAX_LENGTH)]
        public string? settingsJson { get; set; }

        /// <summary>
        /// Whether the connection is active.
        /// </summary>
        public bool isActive { get; set; }
    }

    /// <summary>
    /// Form for updating an existing marketplace connection.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated connection name.
        /// </summary>
        [StringLength(NAME_MAX_LENGTH, MinimumLength = 1)]
        public string? name { get; set; }

        /// <summary>
        /// Updated credentials JSON.
        /// </summary>
        [StringLength(CREDENTIALS_MAX_LENGTH)]
        public string? credentialsJson { get; set; }

        /// <summary>
        /// Updated settings JSON.
        /// </summary>
        [StringLength(SETTINGS_MAX_LENGTH)]
        public string? settingsJson { get; set; }

        /// <summary>
        /// Updated active flag.
        /// </summary>
        public bool? isActive { get; set; }
    }

    /// <summary>
    /// Connection summary DTO for list views.
    /// </summary>
    public struct ConnectionDTO
    {
        /// <summary>
        /// Unique connection identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Connection display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Marketplace adapter key.
        /// </summary>
        public string adapterKey { get; set; }

        /// <summary>
        /// Whether the connection is active.
        /// </summary>
        public bool isActive { get; set; }

        /// <summary>
        /// Timestamp of the last synchronization.
        /// </summary>
        public DateTime? lastSyncAt { get; set; }

        /// <summary>
        /// Number of category mappings configured.
        /// </summary>
        public int categoryMappingsCount { get; set; }

        /// <summary>
        /// Number of linked products.
        /// </summary>
        public int productLinksCount { get; set; }
    }

    /// <summary>
    /// Detailed connection DTO including credentials and settings.
    /// </summary>
    public struct ConnectionDetailDTO
    {
        /// <summary>
        /// Unique connection identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Connection display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Marketplace adapter key.
        /// </summary>
        public string adapterKey { get; set; }

        /// <summary>
        /// JSON-serialized credentials.
        /// </summary>
        public string? credentialsJson { get; set; }

        /// <summary>
        /// JSON-serialized settings.
        /// </summary>
        public string? settingsJson { get; set; }

        /// <summary>
        /// Whether the connection is active.
        /// </summary>
        public bool isActive { get; set; }

        /// <summary>
        /// Timestamp of the last synchronization.
        /// </summary>
        public DateTime? lastSyncAt { get; set; }

        /// <summary>
        /// Timestamp when the connection was created.
        /// </summary>
        public DateTime createdAt { get; set; }

        /// <summary>
        /// Number of category mappings configured.
        /// </summary>
        public int categoryMappingsCount { get; set; }

        /// <summary>
        /// Number of linked products.
        /// </summary>
        public int productLinksCount { get; set; }
    }

    #endregion
}
