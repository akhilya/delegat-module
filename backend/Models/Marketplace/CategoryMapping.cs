using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models.Marketplace;

/// <summary>
/// Maps a local product category to an external marketplace category.
/// </summary>
public class CategoryMapping : IEntity, IMapToDTO<CategoryMapping.MappingDTO>,
    IAddable<CategoryMapping.AddForm>, IUpdateable<CategoryMapping.UpdateForm>
{
    /// <summary>
    /// Unique mapping identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the marketplace connection.
    /// </summary>
    [ForeignKey(nameof(connection))]
    public int connectionId { get; set; }

    /// <summary>
    /// Navigation property to the marketplace connection.
    /// </summary>
    [InverseProperty(nameof(MarketplaceConnection.categoryMappings))]
    public MarketplaceConnection connection { get; set; } = null!;

    /// <summary>
    /// Foreign key to the local category.
    /// </summary>
    [ForeignKey(nameof(localCategory))]
    public int localCategoryId { get; set; }

    /// <summary>
    /// Navigation property to the local category.
    /// </summary>
    public Category localCategory { get; set; } = null!;

    /// <summary>
    /// External category identifier on the marketplace.
    /// </summary>
    [StringLength(EXTERNAL_CATEGORY_ID_MAX_LENGTH)]
    public string externalCategoryId { get; set; } = null!;

    /// <summary>
    /// Cached external category name for display purposes.
    /// </summary>
    [StringLength(EXTERNAL_CATEGORY_NAME_MAX_LENGTH)]
    public string? externalCategoryName { get; set; }

    /// <summary>
    /// Optional display name to distinguish multiple mappings for the same category pair.
    /// </summary>
    [StringLength(NAME_MAX_LENGTH)]
    public string? name { get; set; }

    /// <summary>
    /// Timestamp when the mapping was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// JSON-serialized node graph for the whole-category visual mapping editor
    /// (nodes + edges). When present, the graph is the source of truth for export;
    /// when null, export falls back to per-field <see cref="FieldMapping.modifiersJson"/>
    /// / <see cref="FieldMapping.transformExpression"/>. Stored as LONGTEXT.
    /// </summary>
    public string? graphJson { get; set; }

    /// <summary>
    /// Collection of field mappings for this category mapping.
    /// </summary>
    public virtual ICollection<FieldMapping> fieldMappings { get; set; } = new List<FieldMapping>();

    #region IAddable / IUpdateable

    public bool AddFill(AddForm form)
    {
        connectionId = form.connectionId;
        localCategoryId = form.localCategoryId;
        externalCategoryId = form.externalCategoryId;
        externalCategoryName = form.externalCategoryName;
        name = form.name;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.externalCategoryId))
            externalCategoryId = form.externalCategoryId;
        if (form.externalCategoryName != null)
            externalCategoryName = form.externalCategoryName;
        if (form.name != null)
            name = form.name;
        return true;
    }

    #endregion

    #region IMapToDTO

    MappingDTO IMapToDTO<MappingDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new MappingDTO
        {
            id = Id,
            connectionId = connectionId,
            localCategoryId = localCategoryId,
            localCategoryName = localCategory?.name,
            externalCategoryId = externalCategoryId,
            externalCategoryName = externalCategoryName,
            name = name,
            fieldMappingsCount = fieldMappings?.Count ?? 0,
            hasGraph = !string.IsNullOrEmpty(graphJson),
            createdAt = createdAt
        };
    }

    #endregion

    #region Constants

    /// <summary>
    /// Maximum length for mapping display name.
    /// </summary>
    public const int NAME_MAX_LENGTH = 200;

    /// <summary>
    /// Maximum length for external category identifier.
    /// </summary>
    public const int EXTERNAL_CATEGORY_ID_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for external category name.
    /// </summary>
    public const int EXTERNAL_CATEGORY_NAME_MAX_LENGTH = 500;

    #endregion

    #region Forms & DTOs

    /// <summary>
    /// Internal form used to create a new category mapping (includes connection context).
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Marketplace connection identifier.
        /// </summary>
        public int connectionId { get; set; }

        /// <summary>
        /// Local category identifier.
        /// </summary>
        public int localCategoryId { get; set; }

        /// <summary>
        /// External category identifier on the marketplace.
        /// </summary>
        [StringLength(EXTERNAL_CATEGORY_ID_MAX_LENGTH)]
        public string externalCategoryId { get; set; }

        /// <summary>
        /// External category name (cached for display).
        /// </summary>
        [StringLength(EXTERNAL_CATEGORY_NAME_MAX_LENGTH)]
        public string? externalCategoryName { get; set; }

        /// <summary>
        /// Optional display name for the mapping.
        /// </summary>
        [StringLength(NAME_MAX_LENGTH)]
        public string? name { get; set; }
    }

    /// <summary>
    /// User-facing form for creating a new category mapping.
    /// </summary>
    public struct UserAddForm
    {
        /// <summary>
        /// Local category identifier.
        /// </summary>
        public int localCategoryId { get; set; }

        /// <summary>
        /// External category identifier on the marketplace.
        /// </summary>
        [StringLength(EXTERNAL_CATEGORY_ID_MAX_LENGTH)]
        public string externalCategoryId { get; set; }

        /// <summary>
        /// External category name (cached for display).
        /// </summary>
        [StringLength(EXTERNAL_CATEGORY_NAME_MAX_LENGTH)]
        public string? externalCategoryName { get; set; }

        /// <summary>
        /// Optional display name for the mapping.
        /// </summary>
        [StringLength(NAME_MAX_LENGTH)]
        public string? name { get; set; }
    }

    /// <summary>
    /// Form for updating an existing category mapping.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated external category identifier.
        /// </summary>
        [StringLength(EXTERNAL_CATEGORY_ID_MAX_LENGTH)]
        public string? externalCategoryId { get; set; }

        /// <summary>
        /// Updated external category name.
        /// </summary>
        [StringLength(EXTERNAL_CATEGORY_NAME_MAX_LENGTH)]
        public string? externalCategoryName { get; set; }

        /// <summary>
        /// Updated display name.
        /// </summary>
        [StringLength(NAME_MAX_LENGTH)]
        public string? name { get; set; }

    }

    /// <summary>
    /// Category mapping DTO for API responses.
    /// </summary>
    public struct MappingDTO
    {
        /// <summary>
        /// Unique mapping identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Marketplace connection identifier.
        /// </summary>
        public int connectionId { get; set; }

        /// <summary>
        /// Local category identifier.
        /// </summary>
        public int localCategoryId { get; set; }

        /// <summary>
        /// Local category display name.
        /// </summary>
        public string? localCategoryName { get; set; }

        /// <summary>
        /// External category identifier on the marketplace.
        /// </summary>
        public string externalCategoryId { get; set; }

        /// <summary>
        /// Cached external category name.
        /// </summary>
        public string? externalCategoryName { get; set; }

        /// <summary>
        /// Optional display name for the mapping.
        /// </summary>
        public string? name { get; set; }

        /// <summary>
        /// Number of field mappings configured.
        /// </summary>
        public int fieldMappingsCount { get; set; }

        /// <summary>
        /// Whether a visual node graph is configured for this mapping.
        /// The graph itself is fetched via the dedicated graph endpoint to keep list responses small.
        /// </summary>
        public bool hasGraph { get; set; }

        /// <summary>
        /// Timestamp when the mapping was created.
        /// </summary>
        public DateTime createdAt { get; set; }
    }

    #endregion
}
