using Delegat.Controllers.PublicApi;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class PublicApiStoreContextTests
{
    private sealed class StubCurrentUser : ICurrentUserService
    {
        public int? UserId { get; set; }
        public bool IsAuthenticated => UserId != null;
        public int? GetUserId() => UserId;
        public Task<User?> GetUserAsync(CancellationToken ct = default) => Task.FromResult<User?>(null);
        public int? GetSelectedStoreId() => null;
        public Task<int?> GetSelectedStoreIdAsync(CancellationToken ct = default) => Task.FromResult<int?>(null);
    }

    private static DataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DataContext(options);
    }

    private static User MakeUser(int id, bool active = true) => new()
    {
        Id = id,
        UserName = $"user{id}",
        Email = $"user{id}@example.com",
        NormalizedUserName = $"USER{id}",
        NormalizedEmail = $"USER{id}@EXAMPLE.COM",
        isActive = active,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static Store MakeStore(int id, params User[] members) => new()
    {
        Id = id,
        name = $"Store {id}",
        baseCurrency = "RUB",
        members = members.ToList()
    };

    [Fact]
    public void RequireUserId_WhenMissing_ThrowsUnauthorized()
    {
        using var db = CreateDb();
        var ctx = new PublicApiStoreContext(new StubCurrentUser { UserId = null }, db);

        var ex = Assert.Throws<ApiException>(() => ctx.RequireUserId());
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public void RequireUserId_WhenPresent_ReturnsId()
    {
        using var db = CreateDb();
        var ctx = new PublicApiStoreContext(new StubCurrentUser { UserId = 42 }, db);
        Assert.Equal(42, ctx.RequireUserId());
    }

    [Fact]
    public async Task EnsureStoreAccessAsync_Member_Succeeds()
    {
        await using var db = CreateDb();
        var user = MakeUser(1);
        var store = MakeStore(10, user);
        db.Users.Add(user);
        db.stores.Add(store);
        await db.SaveChangesAsync();

        var ctx = new PublicApiStoreContext(new StubCurrentUser { UserId = 1 }, db);
        await ctx.EnsureStoreAccessAsync(10);
    }

    [Fact]
    public async Task EnsureStoreAccessAsync_NonMember_ThrowsForbidden()
    {
        await using var db = CreateDb();
        var member = MakeUser(1);
        var outsider = MakeUser(2);
        db.Users.AddRange(member, outsider);
        db.stores.Add(MakeStore(10, member));
        await db.SaveChangesAsync();

        var ctx = new PublicApiStoreContext(new StubCurrentUser { UserId = 2 }, db);
        var ex = await Assert.ThrowsAsync<ApiException>(() => ctx.EnsureStoreAccessAsync(10));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureStoreAccessAsync_InactiveMember_ThrowsForbidden()
    {
        await using var db = CreateDb();
        var inactive = MakeUser(3, active: false);
        db.Users.Add(inactive);
        db.stores.Add(MakeStore(11, inactive));
        await db.SaveChangesAsync();

        var ctx = new PublicApiStoreContext(new StubCurrentUser { UserId = 3 }, db);
        var ex = await Assert.ThrowsAsync<ApiException>(() => ctx.EnsureStoreAccessAsync(11));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task RequireStoreAsync_ReturnsUserAndStore()
    {
        await using var db = CreateDb();
        var user = MakeUser(5);
        db.Users.Add(user);
        db.stores.Add(MakeStore(20, user));
        await db.SaveChangesAsync();

        var ctx = new PublicApiStoreContext(new StubCurrentUser { UserId = 5 }, db);
        var (userId, storeId) = await ctx.RequireStoreAsync(20);
        Assert.Equal(5, userId);
        Assert.Equal(20, storeId);
    }
}
