using System.Reflection;
using System.Text.Json;
using Delegat.Controllers;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Delegat.Tests;

public class StoreControllerAccessTests
{
    private static StoreController Create(DataContext db, FakeCurrentUserService current)
    {
        var controller = new StoreController(
            current,
            TestDbFactory.CreateUnitOfWork(db),
            null!,
            null!,
            null!,
            null!);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
    }

    private static (DataContext db, User member, User outsider, User admin, Store own, Store other) Seed()
    {
        var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var member = data.AddUser("member");
        var outsider = data.AddUser("outsider");
        var admin = data.AddUser("owner", isAdmin: true);
        var own = data.AddStore("Own brand", members: member);
        var other = data.AddStore("Other brand", members: outsider);
        db.SaveChanges();
        return (db, member, outsider, admin, own, other);
    }

    private static List<int> ListedStoreIds(IActionResult result, out int total)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        total = json.RootElement.GetProperty("total").GetInt32();
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToList();
    }

    [Theory]
    [InlineData(nameof(StoreController.GetAllStores))]
    [InlineData(nameof(StoreController.GetStoreById))]
    public void StoreReads_DoNotAllowAnonymous(string actionName)
    {
        var method = typeof(StoreController).GetMethod(actionName)!;
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task GetAllStores_Anonymous_ThrowsUnauthorized()
    {
        var (db, _, _, _, _, _) = Seed();
        var controller = Create(db, new FakeCurrentUserService());

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.GetAllStores());

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetAllStores_Member_ReturnsOnlyOwnStores()
    {
        var (db, member, _, _, own, _) = Seed();
        var controller = Create(db, new FakeCurrentUserService { UserId = member.Id, User = member });

        var ids = ListedStoreIds(await controller.GetAllStores(), out var total);

        Assert.Equal(new[] { own.Id }, ids);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetAllStores_Admin_ReturnsEveryStore()
    {
        var (db, _, _, admin, own, other) = Seed();
        var controller = Create(db, new FakeCurrentUserService { UserId = admin.Id, User = admin });

        var ids = ListedStoreIds(await controller.GetAllStores(), out var total);

        Assert.Equal(new[] { own.Id, other.Id }.OrderBy(i => i), ids.OrderBy(i => i));
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task GetStoreById_Anonymous_ThrowsUnauthorized()
    {
        var (db, _, _, _, own, _) = Seed();
        var controller = Create(db, new FakeCurrentUserService());

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.GetStoreById(own.Id));

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task GetStoreById_NonMember_ThrowsNotFound()
    {
        var (db, member, _, _, _, other) = Seed();
        var controller = Create(db, new FakeCurrentUserService { UserId = member.Id, User = member });

        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.GetStoreById(other.Id));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("store_not_found", ex.Code);
    }

    [Fact]
    public async Task GetStoreById_Member_ReturnsStore()
    {
        var (db, member, _, _, own, _) = Seed();
        var controller = Create(db, new FakeCurrentUserService { UserId = member.Id, User = member });

        var ok = Assert.IsType<OkObjectResult>(await controller.GetStoreById(own.Id));

        Assert.Equal(own.Id, Assert.IsType<Store.StoreDTO>(ok.Value).id);
    }

    [Fact]
    public async Task GetStoreById_Admin_ReturnsAnyStore()
    {
        var (db, _, _, admin, _, other) = Seed();
        var controller = Create(db, new FakeCurrentUserService { UserId = admin.Id, User = admin });

        var ok = Assert.IsType<OkObjectResult>(await controller.GetStoreById(other.Id));

        Assert.Equal(other.Id, Assert.IsType<Store.StoreDTO>(ok.Value).id);
    }
}
