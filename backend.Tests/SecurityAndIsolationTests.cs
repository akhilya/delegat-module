using Delegat.Infrastructure.Middleware;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Invoke_CallsNextDelegate()
    {
        var context = new DefaultHttpContext();
        var ran = false;
        var mw = new SecurityHeadersMiddleware(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        await mw.InvokeAsync(context);

        Assert.True(ran);
    }

    [Fact]
    public async Task Invoke_SetsNoindexRobotsTag()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var mw = new SecurityHeadersMiddleware(_ =>
        {
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        });

        await mw.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.Equal("noindex, nofollow, noarchive", context.Response.Headers["X-Robots-Tag"]);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
    }
}

public class StoreIsolationLogicTests
{
    [Fact]
    public async Task Membership_UserSeesOnlyOwnStores()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var alice = data.AddUser("alice");
        var bob = data.AddUser("bob");
        var storeA = data.AddStore("A", members: alice);
        var storeB = data.AddStore("B", members: bob);
        await data.SaveAsync();

        var aliceStores = db.stores.Where(s => s.members.Any(m => m.Id == alice.Id)).Select(s => s.Id).ToList();
        Assert.Equal(new[] { storeA.Id }, aliceStores);
        Assert.DoesNotContain(storeB.Id, aliceStores);
    }

    [Fact]
    public async Task InactiveUser_ExcludedFromMemberCounts()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var active = data.AddUser("active", isActive: true);
        var inactive = data.AddUser("inactive", isActive: false);
        var store = data.AddStore("S", "RUB", "Europe/Moscow", active, inactive);
        await data.SaveAsync();

        var dto = store.MapToDTO(null!);
        Assert.Equal(1, dto.membersCount);
    }
}
