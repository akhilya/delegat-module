namespace Delegat.Models.Marketplace.DTOs;

#region Field-based Grouping

/// <summary>
/// Configuration for field-based grouping (e.g. Ozon).
/// MappingService will inject GroupFieldKey into the product export data.
/// </summary>
/// <param name="GroupFieldKey">Field key for grouping (e.g. "group_id" for Ozon).</param>
/// <param name="MarketplaceGeneratesId">If true, the marketplace generates the group ID itself.</param>
/// <param name="Description">Optional description of how grouping works on this marketplace.</param>
public record FieldBasedGroupingConfig(
    string GroupFieldKey,
    bool MarketplaceGeneratesId = false,
    string? Description = null
);

#endregion

#region Endpoint-based Grouping

/// <summary>
/// External group (from a marketplace).
/// </summary>
/// <param name="ExternalId">External group ID on the marketplace.</param>
/// <param name="Name">Group name.</param>
/// <param name="ProductExternalIds">External IDs of products in this group.</param>
/// <param name="AdditionalData">Additional data from the marketplace.</param>
public record ExternalGroup(
    string ExternalId,
    string? Name,
    List<string> ProductExternalIds,
    Dictionary<string, object>? AdditionalData = null
);

/// <summary>
/// Result of creating a group via an endpoint.
/// </summary>
public record CreateGroupResult(
    bool Success,
    string? ExternalGroupId = null,
    string? ErrorMessage = null,
    Dictionary<string, object>? Details = null
);

/// <summary>
/// Result of modifying group membership.
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
/// Error when adding/removing a product from a group.
/// </summary>
public record GroupMembershipError(
    string ExternalProductId,
    string Message,
    string? Details = null
);

#endregion
