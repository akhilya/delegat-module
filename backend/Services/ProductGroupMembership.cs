using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Data;
using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// A catalog product belongs to at most one group. Adding it to a group
/// moves it out of any other group instead of failing.
/// </summary>
public static class ProductGroupMembership
{
    public static async Task<int> AddOrMoveToGroupAsync(
        DataContext db,
        int storeId,
        int groupId,
        IEnumerable<int>? productIds,
        CancellationToken ct = default)
    {
        var ids = productIds?.Where(id => id > 0).Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0)
            return 0;

        var validIds = await db.products
            .Where(p => p.storeId == storeId && ids.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (validIds.Count == 0)
            return 0;

        var alreadyInGroup = (await db.productGroupMembers
            .Where(m => m.groupId == groupId && validIds.Contains(m.productId))
            .Select(m => m.productId)
            .ToListAsync(ct)).ToHashSet();

        var otherMemberships = await db.productGroupMembers
            .Where(m => validIds.Contains(m.productId) && m.groupId != groupId)
            .ToListAsync(ct);
        db.productGroupMembers.RemoveRange(otherMemberships);

        var maxSortOrder = await db.productGroupMembers
            .Where(m => m.groupId == groupId)
            .Select(m => (int?)m.sortOrder)
            .MaxAsync(ct) ?? -1;

        var added = 0;
        foreach (var productId in ids.Where(validIds.Contains))
        {
            if (alreadyInGroup.Contains(productId))
                continue;

            db.productGroupMembers.Add(new ProductGroupMember
            {
                groupId = groupId,
                productId = productId,
                sortOrder = ++maxSortOrder,
                addedAt = DateTime.UtcNow
            });
            added++;
        }

        if (added > 0 || otherMemberships.Count > 0)
            await db.SaveChangesAsync(ct);

        return added;
    }
}
