namespace PrintO.Marketplace.SDK.DTOs;

#region Field-based Grouping

/// <summary>
/// Configuration for field-based grouping (e.g., Ozon).
/// The MappingService will inject the GroupFieldKey into product export data.
/// </summary>
public record FieldBasedGroupingConfig(
    /// <summary>
    /// The field key to use for grouping (e.g., "group_id" for Ozon)
    /// </summary>
    string GroupFieldKey,

    /// <summary>
    /// If true, the marketplace generates the group ID.
    /// If false, we provide our own (e.g., local group ID or UUID).
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
/// External group representation (from marketplace)
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
    /// External product IDs in this group
    /// </summary>
    List<string> ProductExternalIds,

    /// <summary>
    /// Additional data from marketplace
    /// </summary>
    Dictionary<string, object>? AdditionalData = null
);

/// <summary>
/// Result of creating a group via endpoint
/// </summary>
public record CreateGroupResult(
    /// <summary>
    /// Whether the group was created successfully.
    /// </summary>
    bool Success,

    /// <summary>
    /// External group ID assigned by the marketplace.
    /// </summary>
    string? ExternalGroupId = null,

    /// <summary>
    /// Error message if group creation failed.
    /// </summary>
    string? ErrorMessage = null,

    /// <summary>
    /// Additional details about the operation result.
    /// </summary>
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Result of modifying group membership
/// </summary>
public record GroupMembershipResult(
    /// <summary>
    /// Whether the membership modification succeeded.
    /// </summary>
    bool Success,

    /// <summary>
    /// Number of products added to the group.
    /// </summary>
    int AddedCount = 0,

    /// <summary>
    /// Number of products removed from the group.
    /// </summary>
    int RemovedCount = 0,

    /// <summary>
    /// Number of products that failed to be added/removed.
    /// </summary>
    int FailedCount = 0,

    /// <summary>
    /// Per-product errors for failed membership changes.
    /// </summary>
    List<GroupMembershipError>? Errors = null,

    /// <summary>
    /// General error message if the operation failed.
    /// </summary>
    string? ErrorMessage = null
);

/// <summary>
/// Error when adding/removing product from group
/// </summary>
public record GroupMembershipError(
    /// <summary>
    /// External product ID that caused the error.
    /// </summary>
    string ExternalProductId,

    /// <summary>
    /// Error message.
    /// </summary>
    string Message,

    /// <summary>
    /// Additional error details.
    /// </summary>
    string? Details = null
);

#endregion
