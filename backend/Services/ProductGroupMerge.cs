using System.Data;
using Delegat.Enums;
using Delegat.Infrastructure.Middleware;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>Atomically merges local groups without invoking marketplace adapters.</summary>
public static class ProductGroupMerge
{
    public sealed record Result(int groupId, int mergedGroupCount, int movedProductCount);

    public static async Task<Result> MergeAsync(DataContext db, int storeId, int targetGroupId,
        IReadOnlyCollection<int>? sourceGroupIds, CancellationToken ct = default)
    {
        if (targetGroupId <= 0 || sourceGroupIds == null || sourceGroupIds.Count == 0 ||
            sourceGroupIds.Count > 100 || sourceGroupIds.Any(id => id <= 0 || id == targetGroupId))
            throw ApiException.BadRequest(code: "group_merge_selection_invalid");
        var sources = sourceGroupIds.Distinct().Order().ToList();

        async Task<Result> ApplyAsync(bool acceptAllChangesOnSuccess)
        {
            var ids = sources.Append(targetGroupId).ToList();
            var groups = await db.productGroups.Where(g => g.storeId == storeId && ids.Contains(g.Id))
                .OrderBy(g => g.Id).ToListAsync(ct);
            if (groups.Count != ids.Count)
                throw ApiException.NotFound(code: "group_not_found");
            var target = groups.Single(g => g.Id == targetGroupId);

            // Query memberships directly: product soft-delete filters must not hide members.
            var members = await db.productGroupMembers.Where(m => ids.Contains(m.groupId))
                .OrderBy(m => m.groupId).ThenBy(m => m.sortOrder).ThenBy(m => m.Id).ToListAsync(ct);
            var productIds = members.Select(m => m.productId).Distinct().ToList();
            var validProductCount = await db.products.IgnoreQueryFilters()
                .CountAsync(p => productIds.Contains(p.Id) && p.storeId == storeId, ct);
            if (validProductCount != productIds.Count)
                throw ApiException.BadRequest(code: "group_merge_selection_invalid");

            var links = await db.productGroupMarketplaceLinks.Where(l => ids.Contains(l.productGroupId))
                .OrderBy(l => l.Id).ToListAsync(ct);
            var connectionIds = links.Select(l => l.connectionId).Distinct().ToList();
            if (await db.marketplaceConnections.CountAsync(c => connectionIds.Contains(c.Id) &&
                    c.storeId == storeId, ct) != connectionIds.Count)
                throw ApiException.BadRequest(code: "group_merge_selection_invalid");

            // Validate everything before mutating tracked entities, including in unit tests.
            // Original FK values also make a rolled-back execution-strategy retry
            // recompute the same move rather than treating pending moves as persisted.
            int OriginalGroupId(Models.ProductGroupMember member) =>
                db.Entry(member).Property(m => m.groupId).OriginalValue;
            var targetMembers = members.Where(m => OriginalGroupId(m) == targetGroupId).ToList();
            var present = targetMembers.Select(m => m.productId).ToHashSet();
            var nextOrder = targetMembers.Select(m => (int?)m.sortOrder).Max() ?? -1;
            var addedProducts = members.Where(m => !present.Contains(m.productId))
                .Select(m => m.productId).Distinct().Count();
            if ((long)nextOrder + addedProducts > int.MaxValue)
                throw ApiException.BadRequest(code: "group_merge_selection_invalid");
            var leader = targetMembers.FirstOrDefault(m => m.isVariantLeader)
                ?? members.FirstOrDefault(m => m.isVariantLeader);
            var leaderProductId = leader?.productId;
            var moved = 0;
            foreach (var member in members.Where(m => OriginalGroupId(m) != targetGroupId))
            {
                if (!present.Add(member.productId))
                {
                    db.productGroupMembers.Remove(member);
                    continue;
                }
                member.group = target;
                member.groupId = targetGroupId;
                member.sortOrder = checked(++nextOrder);
                moved++;
            }
            foreach (var member in members.Where(m => db.Entry(m).State != EntityState.Deleted))
                member.isVariantLeader = member.productId == leaderProductId;

            foreach (var group in links.GroupBy(l => l.connectionId))
            {
                // One local group can retain only one external group per connection.
                // Prefer the chosen target, otherwise the oldest source link (query order).
                // Removing redundant local group links never invokes a remote adapter
                // and does not affect individual product marketplace links.
                var retained = group.FirstOrDefault(l =>
                    db.Entry(l).Property(x => x.productGroupId).OriginalValue == targetGroupId) ?? group.First();
                db.productGroupMarketplaceLinks.RemoveRange(group.Where(l => l != retained));
                retained.productGroup = target;
                retained.productGroupId = targetGroupId;
                retained.status = ProductMarketplaceStatus.Modified;
            }

            target.updatedAt = DateTime.UtcNow;
            // Fix up moved relationships before deleting the now-empty source groups.
            db.ChangeTracker.DetectChanges();
            db.productGroups.RemoveRange(groups.Where(g => g.Id != targetGroupId));
            await db.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
            return new(targetGroupId, sources.Count, moved);
        }

        if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null)
            return await ApplyAsync(true);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await ApplyAsync(false);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.AcceptAllChanges();
            return result;
        });
    }
}
