using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Service for synchronizing product groups with marketplaces (export, import, membership).
/// </summary>
public class GroupSyncService
{
    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly ILogger<GroupSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GroupSyncService"/>.
    /// </summary>
    public GroupSyncService(
        DataContext context,
        IMarketplaceAdapterRegistry adapterRegistry,
        ILogger<GroupSyncService> logger)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
        _logger = logger;
    }

    #region Export Groups

    /// <summary>
    /// Export a product group to a marketplace.
    /// Field-based: creates a link; MappingService adds group_id during product export.
    /// Endpoint-based: calls the marketplace API to create the group.
    /// </summary>
    public async Task<GroupExportResult> ExportGroupAsync(
        int productGroupId,
        int connectionId,
        CancellationToken ct = default)
    {
        // Load group with members and their marketplace links
        var group = await _context.productGroups
            .Include(g => g.members)
                .ThenInclude(m => m.product)
                    .ThenInclude(p => p.marketplaceLinks)
            .FirstOrDefaultAsync(g => g.Id == productGroupId, ct);

        if (group == null)
            return GroupExportResult.Failure($"Group with ID {productGroupId} not found");

        // Load connection
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return GroupExportResult.Failure($"Connection with ID {connectionId} not found");

        if (!connection.isActive)
            return GroupExportResult.Failure("Connection is inactive");

        // Get adapter
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return GroupExportResult.Failure($"Adapter '{connection.adapterKey}' not found");

        // Check for existing group link
        var existingLink = await _context.productGroupMarketplaceLinks
            .FirstOrDefaultAsync(l =>
                l.productGroupId == productGroupId &&
                l.connectionId == connectionId, ct);

        try
        {
            // Determine grouping type
            var fieldConfig = adapter.GetFieldBasedGroupingConfig();
            var isFieldBased = fieldConfig != null &&
                adapter.Capabilities.HasFlag(MarketplaceCapabilities.FieldBasedGrouping);
            var isEndpointBased = adapter.Capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping);

            if (!isFieldBased && !isEndpointBased)
                return GroupExportResult.Failure("Adapter does not support product grouping");

            if (isFieldBased)
            {
                return await ExportFieldBasedGroupAsync(
                    group, connection, existingLink, fieldConfig!, ct);
            }
            else
            {
                return await ExportEndpointBasedGroupAsync(
                    group, connection, adapter, existingLink, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting group {GroupId} to connection {ConnectionId}",
                productGroupId, connectionId);

            // Update link status on error
            if (existingLink != null)
            {
                existingLink.UpdateFill(new ProductGroupMarketplaceLink.UpdateForm
                {
                    status = ProductMarketplaceStatus.SyncError,
                    lastErrorMessage = ex.Message,
                    lastSyncAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(ct);
            }

            return GroupExportResult.Failure($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Field-based export: creates a link with group_id; MappingService adds the field during product export.
    /// </summary>
    private async Task<GroupExportResult> ExportFieldBasedGroupAsync(
        ProductGroup group,
        MarketplaceConnection connection,
        ProductGroupMarketplaceLink? existingLink,
        FieldBasedGroupingConfig config,
        CancellationToken ct)
    {
        // For field-based grouping we use the local group ID as external ID
        // (or we can generate a UUID if the marketplace requires it)
        var externalGroupId = existingLink?.externalGroupId ??
            (config.MarketplaceGeneratesId ? Guid.NewGuid().ToString() : group.Id.ToString());

        if (existingLink == null)
        {
            existingLink = new ProductGroupMarketplaceLink();
            existingLink.AddFill(new ProductGroupMarketplaceLink.AddForm
            {
                productGroupId = group.Id,
                connectionId = connection.Id,
                externalGroupId = externalGroupId,
                externalGroupName = group.name,
                status = ProductMarketplaceStatus.Synced
            });
            _context.productGroupMarketplaceLinks.Add(existingLink);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Created group link {GroupId} with connection {ConnectionId}, " +
                "external_group_id={ExternalGroupId} (field-based)",
                group.Id, connection.Id, externalGroupId);

            return GroupExportResult.Ok(
                existingLink.Id,
                externalGroupId,
                isNew: true,
                isFieldBased: true,
                message: $"Group is ready for synchronization. Re-export group products to apply group_id.");
        }
        else
        {
            existingLink.UpdateFill(new ProductGroupMarketplaceLink.UpdateForm
            {
                externalGroupName = group.name,
                status = ProductMarketplaceStatus.Synced,
                lastSyncAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);

            return GroupExportResult.Ok(
                existingLink.Id,
                externalGroupId,
                isNew: false,
                isFieldBased: true,
                message: "Group link updated.");
        }
    }

    /// <summary>
    /// Endpoint-based export: calls the marketplace API to create the group.
    /// </summary>
    private async Task<GroupExportResult> ExportEndpointBasedGroupAsync(
        ProductGroup group,
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        ProductGroupMarketplaceLink? existingLink,
        CancellationToken ct)
    {
        // Collect external IDs of group products
        var productExternalIds = new List<string>();
        var missingProducts = new List<string>();

        foreach (var member in group.members.OrderBy(m => m.sortOrder))
        {
            var productLink = member.product.marketplaceLinks
                .FirstOrDefault(l => l.connectionId == connection.Id);

            if (productLink == null || string.IsNullOrEmpty(productLink.externalProductId))
            {
                missingProducts.Add(member.product.sku ?? member.product.name);
            }
            else
            {
                productExternalIds.Add(productLink.externalProductId);
            }
        }

        if (missingProducts.Any())
        {
            return GroupExportResult.Failure(
                $"Not all group products have been exported to the marketplace. " +
                $"Export these products first: {string.Join(", ", missingProducts.Take(5))}" +
                (missingProducts.Count > 5 ? $" and {missingProducts.Count - 5} more..." : ""));
        }

        if (!productExternalIds.Any())
        {
            return GroupExportResult.Failure("Group contains no products");
        }

        // If a link already exists - update group membership
        if (existingLink != null && !string.IsNullOrEmpty(existingLink.externalGroupId))
        {
            // Get current products in the marketplace group
            // TODO: can be optimized by comparing only changes

            // For now just update the status
            existingLink.UpdateFill(new ProductGroupMarketplaceLink.UpdateForm
            {
                externalGroupName = group.name,
                status = ProductMarketplaceStatus.Synced,
                lastSyncAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Updated group link {GroupId} with connection {ConnectionId}",
                group.Id, connection.Id);

            return GroupExportResult.Ok(
                existingLink.Id,
                existingLink.externalGroupId,
                isNew: false,
                isFieldBased: false);
        }

        // Create a new group via API
        var result = await adapter.CreateGroupAsync(
            connection, productExternalIds, group.name, ct);

        if (!result.Success)
        {
            return GroupExportResult.Failure(result.ErrorMessage ?? "Error creating group");
        }

        // Create link
        existingLink = new ProductGroupMarketplaceLink();
        existingLink.AddFill(new ProductGroupMarketplaceLink.AddForm
        {
            productGroupId = group.Id,
            connectionId = connection.Id,
            externalGroupId = result.ExternalGroupId!,
            externalGroupName = group.name,
            status = ProductMarketplaceStatus.Synced
        });
        _context.productGroupMarketplaceLinks.Add(existingLink);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created group {GroupId} on connection {ConnectionId}, " +
            "external_group_id={ExternalGroupId} (endpoint-based)",
            group.Id, connection.Id, result.ExternalGroupId);

        return GroupExportResult.Ok(
            existingLink.Id,
            result.ExternalGroupId!,
            isNew: true,
            isFieldBased: false);
    }

    #endregion

    #region Modify Group Membership

    /// <summary>
    /// Add products to a marketplace group (endpoint-based adapters).
    /// </summary>
    public async Task<GroupMembershipResult> AddProductsToMarketplaceGroupAsync(
        int productGroupId,
        int connectionId,
        List<int> productIds,
        CancellationToken ct = default)
    {
        var groupLink = await _context.productGroupMarketplaceLinks
            .FirstOrDefaultAsync(l =>
                l.productGroupId == productGroupId &&
                l.connectionId == connectionId, ct);

        if (groupLink == null || string.IsNullOrEmpty(groupLink.externalGroupId))
        {
            return new GroupMembershipResult(false, ErrorMessage: "Group is not exported to the marketplace");
        }

        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return new GroupMembershipResult(false, ErrorMessage: "Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return new GroupMembershipResult(false, ErrorMessage: "Adapter not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ModifyGroupMembership))
            return new GroupMembershipResult(false, ErrorMessage: "Adapter does not support modifying group membership");

        // Get external IDs of products
        var productLinks = await _context.productMarketplaceLinks
            .Where(l => productIds.Contains(l.productId) && l.connectionId == connectionId)
            .ToListAsync(ct);

        var externalIds = productLinks
            .Where(l => !string.IsNullOrEmpty(l.externalProductId))
            .Select(l => l.externalProductId!)
            .ToList();

        if (!externalIds.Any())
        {
            return new GroupMembershipResult(false, ErrorMessage: "Products are not exported to the marketplace");
        }

        return await adapter.AddProductsToGroupAsync(connection, groupLink.externalGroupId, externalIds, ct);
    }

    /// <summary>
    /// Remove products from a marketplace group (endpoint-based adapters).
    /// </summary>
    public async Task<GroupMembershipResult> RemoveProductsFromMarketplaceGroupAsync(
        int productGroupId,
        int connectionId,
        List<int> productIds,
        CancellationToken ct = default)
    {
        var groupLink = await _context.productGroupMarketplaceLinks
            .FirstOrDefaultAsync(l =>
                l.productGroupId == productGroupId &&
                l.connectionId == connectionId, ct);

        if (groupLink == null || string.IsNullOrEmpty(groupLink.externalGroupId))
        {
            return new GroupMembershipResult(false, ErrorMessage: "Group is not exported to the marketplace");
        }

        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return new GroupMembershipResult(false, ErrorMessage: "Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return new GroupMembershipResult(false, ErrorMessage: "Adapter not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ModifyGroupMembership))
            return new GroupMembershipResult(false, ErrorMessage: "Adapter does not support modifying group membership");

        // Get external IDs of products
        var productLinks = await _context.productMarketplaceLinks
            .Where(l => productIds.Contains(l.productId) && l.connectionId == connectionId)
            .ToListAsync(ct);

        var externalIds = productLinks
            .Where(l => !string.IsNullOrEmpty(l.externalProductId))
            .Select(l => l.externalProductId!)
            .ToList();

        if (!externalIds.Any())
        {
            return new GroupMembershipResult(false, ErrorMessage: "Products not found on the marketplace");
        }

        return await adapter.RemoveProductsFromGroupAsync(connection, groupLink.externalGroupId, externalIds, ct);
    }

    #endregion

    #region Import Groups

    /// <summary>
    /// Import product groups from a marketplace.
    /// </summary>
    public async Task<GroupImportResult> ImportGroupsAsync(
        int connectionId,
        int storeId,
        CancellationToken ct = default)
    {
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return GroupImportResult.Failure("Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return GroupImportResult.Failure("Adapter not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportGroups))
            return GroupImportResult.Failure("Adapter does not support group import");

        int importedCount = 0;
        int updatedCount = 0;
        string? cursor = null;

        do
        {
            var result = await adapter.ImportGroupsAsync(connection, 100, cursor, ct);

            foreach (var externalGroup in result.Items)
            {
                var (imported, updated) = await ProcessImportedGroupAsync(
                    externalGroup, connection, storeId, ct);
                if (imported) importedCount++;
                if (updated) updatedCount++;
            }

            cursor = result.NextCursor;

            if (!result.HasMore) break;
        }
        while (!ct.IsCancellationRequested);

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Group import from connection {ConnectionId}: imported {Imported}, updated {Updated}",
            connectionId, importedCount, updatedCount);

        return GroupImportResult.Ok(importedCount, updatedCount);
    }

    private async Task<(bool imported, bool updated)> ProcessImportedGroupAsync(
        ExternalGroup externalGroup,
        MarketplaceConnection connection,
        int storeId,
        CancellationToken ct)
    {
        // Search for existing link
        var existingLink = await _context.productGroupMarketplaceLinks
            .Include(l => l.productGroup)
            .FirstOrDefaultAsync(l =>
                l.externalGroupId == externalGroup.ExternalId &&
                l.connectionId == connection.Id, ct);

        if (existingLink != null)
        {
            // Update existing link
            existingLink.UpdateFill(new ProductGroupMarketplaceLink.UpdateForm
            {
                externalGroupName = externalGroup.Name,
                status = ProductMarketplaceStatus.Synced,
                lastSyncAt = DateTime.UtcNow
            });
            return (false, true);
        }

        // Create a new local group
        var group = new ProductGroup();
        group.AddFill(new ProductGroup.AddForm
        {
            name = externalGroup.Name ?? $"Group {externalGroup.ExternalId}",
            storeId = storeId
        });
        _context.productGroups.Add(group);
        await _context.SaveChangesAsync(ct);

        // Create link
        var link = new ProductGroupMarketplaceLink();
        link.AddFill(new ProductGroupMarketplaceLink.AddForm
        {
            productGroupId = group.Id,
            connectionId = connection.Id,
            externalGroupId = externalGroup.ExternalId,
            externalGroupName = externalGroup.Name,
            status = ProductMarketplaceStatus.Synced
        });
        _context.productGroupMarketplaceLinks.Add(link);

        // Link products to the group (if they are already imported)
        if (externalGroup.ProductExternalIds.Any())
        {
            var productLinks = await _context.productMarketplaceLinks
                .Where(pl =>
                    pl.connectionId == connection.Id &&
                    externalGroup.ProductExternalIds.Contains(pl.externalProductId))
                .ToListAsync(ct);

            int sortOrder = 0;
            foreach (var pl in productLinks)
            {
                var member = new ProductGroupMember();
                member.AddFill(new ProductGroupMember.AddForm
                {
                    groupId = group.Id,
                    productId = pl.productId,
                    sortOrder = sortOrder++
                });
                _context.productGroupMembers.Add(member);
            }
        }

        return (true, false);
    }

    #endregion

    #region Delete Groups

    /// <summary>
    /// Delete a product group on the marketplace.
    /// </summary>
    public async Task<ExportResult> DeleteMarketplaceGroupAsync(
        int productGroupId,
        int connectionId,
        CancellationToken ct = default)
    {
        var groupLink = await _context.productGroupMarketplaceLinks
            .FirstOrDefaultAsync(l =>
                l.productGroupId == productGroupId &&
                l.connectionId == connectionId, ct);

        if (groupLink == null)
        {
            return new ExportResult(true, null, null, null); // Nothing to delete
        }

        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return new ExportResult(false, null, "Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return new ExportResult(false, null, "Adapter not found");

        // For field-based grouping just remove the link
        var fieldConfig = adapter.GetFieldBasedGroupingConfig();
        if (fieldConfig != null)
        {
            _context.productGroupMarketplaceLinks.Remove(groupLink);
            await _context.SaveChangesAsync(ct);
            return new ExportResult(true, null, null, null);
        }

        // For endpoint-based call the API
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.DeleteGroups))
        {
            // Just remove the link if deletion on the marketplace is not supported
            _context.productGroupMarketplaceLinks.Remove(groupLink);
            await _context.SaveChangesAsync(ct);
            return new ExportResult(true, null, null, null);
        }

        var result = await adapter.DeleteGroupAsync(connection, groupLink.externalGroupId, ct);

        if (result.Success)
        {
            _context.productGroupMarketplaceLinks.Remove(groupLink);
            await _context.SaveChangesAsync(ct);
        }

        return result;
    }

    #endregion

    #region Get Group Info

    /// <summary>
    /// Get the group_id for a product (for field-based grouping)
    /// </summary>
    public async Task<string?> GetProductGroupIdForExportAsync(
        int productId,
        int connectionId,
        CancellationToken ct = default)
    {
        // Find the group the product belongs to
        var membership = await _context.productGroupMembers
            .Include(m => m.group)
                .ThenInclude(g => g.marketplaceLinks)
            .FirstOrDefaultAsync(m => m.productId == productId, ct);

        if (membership == null)
            return null;

        // Find the group's marketplace link
        var groupLink = membership.group.marketplaceLinks
            .FirstOrDefault(l => l.connectionId == connectionId);

        return groupLink?.externalGroupId;
    }

    #endregion
}

#region Result Types

public record GroupExportResult(
    bool IsSuccess,
    int? LinkId = null,
    string? ExternalGroupId = null,
    bool IsNew = false,
    bool IsFieldBased = false,
    string? ErrorMessage = null,
    string? Message = null)
{
    public static GroupExportResult Failure(string message) =>
        new(false, ErrorMessage: message);

    public static GroupExportResult Ok(
        int linkId,
        string externalGroupId,
        bool isNew,
        bool isFieldBased,
        string? message = null) =>
        new(true, linkId, externalGroupId, isNew, isFieldBased, Message: message);
}

public record GroupImportResult(
    bool IsSuccess,
    int ImportedCount = 0,
    int UpdatedCount = 0,
    int SkippedCount = 0,
    string? ErrorMessage = null,
    List<string>? Errors = null)
{
    public static GroupImportResult Failure(string message) =>
        new(false, ErrorMessage: message);

    public static GroupImportResult Ok(int imported, int updated, int skipped = 0) =>
        new(true, imported, updated, skipped);
}

#endregion
