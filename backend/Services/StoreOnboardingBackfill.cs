using Microsoft.EntityFrameworkCore;
using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// One-shot: existing stores were created before onboarding tracking.
/// Marks them complete so current tenants are not forced back into Setup.
/// </summary>
public static class StoreOnboardingBackfill
{
    public const string Key = "onboarding-completed-backfill";

    public static async Task RunAsync(DataContext db, CancellationToken ct = default)
    {
        if (await db.systemVersions.AnyAsync(sv => sv.key == Key, ct))
            return;

        var now = DateTime.UtcNow;
        var incomplete = await db.stores
            .Where(s => s.onboardingCompletedAt == null)
            .ToListAsync(ct);

        foreach (var store in incomplete)
            store.onboardingCompletedAt = now;

        db.systemVersions.Add(new SystemVersion
        {
            key = Key,
            version = "1",
            appliedAt = now,
        });

        await db.SaveChangesAsync(ct);
    }
}
