using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure;
using Delegat.Models;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Enqueues marketplace category imports for <see cref="CategoryImportBackgroundService"/>.
/// Dedupes in-flight jobs per connection + external category.
/// </summary>
public sealed class CategoryImportQueueService
{
    private readonly DataContext _db;

    public CategoryImportQueueService(DataContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns an existing in-flight job, resets a failed job, or inserts a new row.
    /// Returns null when a mapping already exists (nothing to import).
    /// </summary>
    public async Task<PendingCategoryImport?> EnqueueIfNeededAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        string? externalCategoryName,
        string? localCategoryName,
        int? localCategoryId,
        IReadOnlyList<string>? selectedFieldKeys,
        string operationId,
        int authorUserId,
        CancellationToken ct = default,
        IReadOnlyList<CategoryImportAttributeDecision>? attributeDecisions = null)
    {
        var extId = (externalCategoryId ?? "").Trim();
        if (extId.Length == 0)
            throw new ArgumentException("External category ID is required", nameof(externalCategoryId));

        extId = StringClip.ClipRequired(extId, CategoryMapping.EXTERNAL_CATEGORY_ID_MAX_LENGTH, extId);

        var mappingQuery = _db.categoryMappings.AsNoTracking()
            .Where(m => m.connectionId == connection.Id && m.externalCategoryId == extId);
        if (localCategoryId is int localId)
            mappingQuery = mappingQuery.Where(m => m.localCategoryId == localId);
        if (await mappingQuery.AnyAsync(ct))
            return null;

        var active = await _db.pendingCategoryImports
            .Where(j => j.connectionId == connection.Id
                        && j.externalCategoryId == extId
                        && (j.status == CategoryImportStatus.Pending
                            || j.status == CategoryImportStatus.InProgress))
            .OrderBy(j => j.Id)
            .FirstOrDefaultAsync(ct);
        if (active != null)
            return active;

        var failed = await _db.pendingCategoryImports
            .Where(j => j.connectionId == connection.Id
                        && j.externalCategoryId == extId
                        && j.status == CategoryImportStatus.Failed)
            .OrderByDescending(j => j.Id)
            .FirstOrDefaultAsync(ct);

        var name = StringClip.ClipOptional(
            MarketplaceCategoryLabel.ForLocalCategory(extId, localCategoryName, externalCategoryName),
            Category.CATEGORY_NAME_MAX_LENGTH);
        var extName = string.IsNullOrWhiteSpace(externalCategoryName)
            ? null
            : StringClip.Clip(externalCategoryName, CategoryMapping.EXTERNAL_CATEGORY_NAME_MAX_LENGTH);
        var keysJson = selectedFieldKeys == null
            ? null
            : JsonSerializer.Serialize(selectedFieldKeys);
        var decisionsJson = attributeDecisions == null
            ? null
            : JsonSerializer.Serialize(attributeDecisions);

        if (failed != null)
        {
            failed.status = CategoryImportStatus.Pending;
            failed.lastError = null;
            failed.retryCount = 0;
            failed.operationId = operationId;
            failed.authorUserId = authorUserId;
            failed.localCategoryName = name;
            failed.localCategoryId = localCategoryId;
            failed.externalCategoryName = extName;
            failed.selectedFieldKeysJson = keysJson;
            failed.attributeDecisionsJson = decisionsJson;
            failed.lastAttemptAt = null;
            failed.completedAt = null;
            await _db.SaveChangesAsync(ct);
            return failed;
        }

        var row = new PendingCategoryImport
        {
            connectionId = connection.Id,
            storeId = connection.storeId,
            authorUserId = authorUserId,
            operationId = operationId,
            externalCategoryId = extId,
            externalCategoryName = extName,
            localCategoryName = name,
            localCategoryId = localCategoryId,
            selectedFieldKeysJson = keysJson,
            attributeDecisionsJson = decisionsJson,
            status = CategoryImportStatus.Pending,
            createdAt = DateTime.UtcNow,
        };
        _db.pendingCategoryImports.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }
}
