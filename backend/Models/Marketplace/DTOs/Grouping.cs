namespace Delegat.Models.Marketplace.DTOs;

#region Field-based Grouping

/// <summary>
/// Configuration for field-based grouping (e.g. Ozon).
/// MappingService will inject GroupFieldKey into the product export data.
/// </summary>
public record FieldBasedGroupingConfig(
    /// <summary>
    /// Field key for grouping (e.g. "group_id" for Ozon)
    /// </summary>
    string GroupFieldKey,

    /// <summary>
    /// If true, the marketplace generates the group ID itself.
    /// If false, we provide our own (e.g. local group ID or UUID).
    /// </summary>
    bool MarketplaceGeneratesId = false,

    /// <summary>
    /// Optional description of how grouping works on this marketplace
    /// </summary>
    string? Description = null
);

#endregion

#region Endpoint-based Grouping

/// <summary>
/// External group (from a marketplace)
/// </summary>
public record ExternalGroup(
    /// <summary>
    /// External group ID on the marketplace
    /// </summary>
    string ExternalId,

    /// <summary>
    /// Group name
    /// </summary>
    string? Name,

    /// <summary>
    /// External IDs of products in this group
    /// </summary>
    List<string> ProductExternalIds,

    /// <summary>
    /// Additional data from the marketplace
    /// </summary>
    Dictionary<string, object>? AdditionalData = null
);

/// <summary>
/// Result of creating a group via an endpoint
/// </summary>
public record CreateGroupResult(
    bool Success,
    string? ExternalGroupId = null,
    string? ErrorMessage = null,
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Result of modifying group membership
/// </summary>
public record GroupMembershipResult(
    bool Success,
    int AddedCount = 0,
    int RemovedCount = 0,
    int FailedCount = 0,
    List<GroupMembershipError>? Errors = null,
    string? ErrorMessage = null
);

/// <summary>
/// Error when adding/removing a product from a group
/// </summary>
public record GroupMembershipError(
    string ExternalProductId,
    string Message,
    string? Details = null
);

#endregion
