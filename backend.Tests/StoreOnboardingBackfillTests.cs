using Delegat.Services;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class StoreOnboardingBackfillTests
{
    [Fact]
    public async Task RunAsync_CompletesExistingIncompleteStoresOnce()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        store.onboardingCompletedAt = null;
        await data.SaveAsync();

        await StoreOnboardingBackfill.RunAsync(db);

        var saved = await db.stores.SingleAsync();
        Assert.NotNull(saved.onboardingCompletedAt);
        Assert.True(await db.systemVersions.AnyAsync(v => v.key == StoreOnboardingBackfill.Key));

        var stamped = saved.onboardingCompletedAt;
        await StoreOnboardingBackfill.RunAsync(db);
        Assert.Equal(stamped, (await db.stores.SingleAsync()).onboardingCompletedAt);
    }

    [Fact]
    public async Task RunAsync_LeavesStoresAloneWhenAlreadyStampedAndComplete()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var doneAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        store.onboardingCompletedAt = doneAt;
        db.systemVersions.Add(new Delegat.Models.SystemVersion
        {
            key = StoreOnboardingBackfill.Key,
            version = "1",
            appliedAt = doneAt,
        });
        await data.SaveAsync();

        await StoreOnboardingBackfill.RunAsync(db);

        Assert.Equal(doneAt, (await db.stores.SingleAsync()).onboardingCompletedAt);
    }
}
