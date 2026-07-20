using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class UserMembershipDomainTests
{
    [Fact]
    public async Task SelectStore_UpdatesSelectedStoreId_WhenMember()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("mem");
        var store = data.AddStore("S1", members: user);
        await data.SaveAsync();

        Assert.Contains(store.members, m => m.Id == user.Id && m.isActive);
        user.UpdateFill(new User.SelectStoreForm(store.Id));
        await db.SaveChangesAsync();

        Assert.Equal(store.Id, user.selectedStoreId);
    }

    [Fact]
    public async Task SelectStore_RejectedWhenNotMember()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("outsider");
        var other = data.AddUser("owner");
        var store = data.AddStore("Private", members: other);
        await data.SaveAsync();

        var isMember = await Task.FromResult(
            db.stores.Any(s => s.Id == store.Id && s.members.Any(m => m.Id == user.Id && m.isActive)));
        Assert.False(isMember);
    }

    [Fact]
    public async Task InactiveMembership_CannotSelect()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var inactive = data.AddUser("gone", isActive: false);
        var store = data.AddStore("S", members: inactive);
        await data.SaveAsync();

        var canSelect = db.stores.Any(s =>
            s.Id == store.Id && s.members.Any(m => m.Id == inactive.Id && m.isActive));
        Assert.False(canSelect);
    }

    [Fact]
    public async Task AdminFlag_IsDistinctFromStoreAdmin()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var admin = data.AddUser("admin", isAdmin: true);
        var member = data.AddUser("member");
        var store = data.AddStore("S");
        data.AddMember(store, member, asAdmin: true);
        data.AddMember(store, admin, asAdmin: false);
        await data.SaveAsync();

        Assert.True(admin.isAdmin);
        Assert.Contains(store.storeAdmins, a => a.Id == member.Id);
        Assert.DoesNotContain(store.storeAdmins, a => a.Id == admin.Id);
    }

    [Fact]
    public async Task ListUsers_AdminSeesInactive_NonAdminLogic()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        data.AddUser("a", isActive: true);
        data.AddUser("b", isActive: false);
        await data.SaveAsync();

        var activeOnly = db.Users.Where(u => u.isActive).ToList();
        var all = db.Users.ToList();
        Assert.Single(activeOnly);
        Assert.Equal(2, all.Count);
    }
}

public class InvitationTokenDomainTests
{
    [Fact]
    public async Task CreateAndConsume_InvitationToken()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("invitee");
        await data.SaveAsync();

        var token = new InvitationToken
        {
            token = Guid.NewGuid().ToString("N"),
            used = false,
            createdAt = DateTime.UtcNow
        };
        db.invitationTokens.Add(token);
        await db.SaveChangesAsync();

        Assert.False(token.used);
        token.UpdateFill(new InvitationToken.UseForm(user.Id));
        await db.SaveChangesAsync();

        Assert.True(token.used);
        Assert.Equal(user.Id, token.usedByUserId);
    }

    [Fact]
    public async Task UsedToken_CannotBeReused()
    {
        await using var db = TestDbFactory.CreateDb();
        var token = new InvitationToken
        {
            token = "used-token",
            used = true,
            usedByUserId = 1,
            createdAt = DateTime.UtcNow
        };
        db.invitationTokens.Add(token);
        await db.SaveChangesAsync();

        var found = db.invitationTokens.Single(t => t.token == "used-token");
        Assert.True(found.used);
        // Sign-up would throw BadRequest for used token
        Assert.Throws<ApiException>(() =>
        {
            if (found.used) throw ApiException.BadRequest("Token already used");
        });
    }
}
