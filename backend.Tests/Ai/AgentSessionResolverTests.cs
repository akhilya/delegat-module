using Delegat.Infrastructure.Auth;
using Delegat.Models;
using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests.Ai;

/// <summary>
/// The resolver must authorize from current database facts: a revoked
/// membership, demoted admin, or deactivated actor in another context is denied
/// even when the requesting context still tracks stale navigation state.
/// </summary>
public class AgentSessionResolverTests
{
    private static AgentSessionResolver Resolver(
        DataContext db,
        FakeCurrentUserService current,
        IPermissionService? permissions = null) =>
        new(current,
            permissions ?? new GrantAllPermissions(),
            db,
            new HttpContextAccessor());

    [Fact]
    public async Task ResolveAsync_MemberRevokedInOtherContext_Denied()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();

            // Deliberately stale tracked identity: the fake returns this User
            // whose memberships navigation was fixed up before the revocation.
            var stale = await db.Users
                .Include(u => u.memberships)
                .SingleAsync(u => u.Id == user.Id);
            var current = new FakeCurrentUserService { UserId = user.Id, User = stale };

            var session = await Resolver(db, current).ResolveAsync(store.Id, default);
            Assert.Equal(user.Id, session.UserId);

            await using var other = TestDbFactory.CreateSqliteContext(TestDbFactory.SqliteDsn(db));
            var storeOther = await other.stores
                .Include(s => s.members)
                .SingleAsync(s => s.Id == store.Id);
            storeOther.members.Remove(storeOther.members.Single(m => m.Id == user.Id));
            await other.SaveChangesAsync();

            // The tracker still believes in the membership; the resolver must not.
            Assert.Contains(stale.memberships, s => s.Id == store.Id);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => Resolver(db, current).ResolveAsync(store.Id, default));
        }
    }

    [Fact]
    public async Task ResolveForUserAsync_MemberRevokedInOtherContext_Denied()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();
            var current = new FakeCurrentUserService();

            var session = await Resolver(db, current)
                .ResolveForUserAsync(user.Id, store.Id, default);
            Assert.Equal(store.Id, session.StoreId);

            await using var other = TestDbFactory.CreateSqliteContext(TestDbFactory.SqliteDsn(db));
            var storeOther = await other.stores
                .Include(s => s.members)
                .SingleAsync(s => s.Id == store.Id);
            storeOther.members.Remove(storeOther.members.Single(m => m.Id == user.Id));
            await other.SaveChangesAsync();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => Resolver(db, current).ResolveForUserAsync(user.Id, store.Id, default));
        }
    }

    [Fact]
    public async Task ResolveForUserAsync_AdminDemotedInOtherContext_Denied()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser(isAdmin: true);
            var store = data.AddStore();
            await data.SaveAsync();

            // Tracked entity keeps isAdmin=true after the other context demotes.
            var stale = await db.Users.SingleAsync(u => u.Id == user.Id);
            var current = new FakeCurrentUserService();

            var session = await Resolver(db, current)
                .ResolveForUserAsync(user.Id, store.Id, default);
            Assert.Equal(user.Id, session.UserId);

            await using var other = TestDbFactory.CreateSqliteContext(TestDbFactory.SqliteDsn(db));
            (await other.Users.SingleAsync(u => u.Id == user.Id)).isAdmin = false;
            await other.SaveChangesAsync();

            Assert.True(stale.isAdmin);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => Resolver(db, current).ResolveForUserAsync(user.Id, store.Id, default));
        }
    }

    [Fact]
    public async Task ResolveForUserAsync_DeactivatedActor_Denied()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();
            var current = new FakeCurrentUserService();

            await Resolver(db, current).ResolveForUserAsync(user.Id, store.Id, default);

            await using var other = TestDbFactory.CreateSqliteContext(TestDbFactory.SqliteDsn(db));
            (await other.Users.SingleAsync(u => u.Id == user.Id)).isActive = false;
            await other.SaveChangesAsync();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => Resolver(db, current).ResolveForUserAsync(user.Id, store.Id, default));
        }
    }

    [Fact]
    public async Task ResolveForUserAsync_ActiveAdminWithoutMembership_Allowed()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser(isAdmin: true);
            var store = data.AddStore();
            await data.SaveAsync();
            var current = new FakeCurrentUserService();

            var session = await Resolver(db, current)
                .ResolveForUserAsync(user.Id, store.Id, default);
            Assert.Equal(user.Id, session.UserId);
        }
    }

    [Fact]
    public async Task ResolveForUserAsync_ActiveMember_Allowed()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();
            var current = new FakeCurrentUserService();

            var session = await Resolver(db, current)
                .ResolveForUserAsync(user.Id, store.Id, default);
            Assert.Equal(store.Id, session.StoreId);
            Assert.Contains(Permissions.AiUse, session.Permissions);
        }
    }

    [Fact]
    public async Task ResolveAsync_NoAgentPermission_Denied()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using (db)
        {
            var data = new TestDataBuilder(db);
            var user = data.AddUser();
            var store = data.AddStore(members: user);
            await data.SaveAsync();
            var current = new FakeCurrentUserService { UserId = user.Id };

            // files.manage alone is not an agent surface: no ai.use, no workspace.view.
            var resolver = Resolver(db, current,
                new StaticPermissions(Permissions.FilesManage));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => resolver.ResolveAsync(store.Id, default));
        }
    }
}
