using System.Security.Claims;
using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Delegat.Tests;

public class AccountSecurityTotpTests
{
    private const string Password = "Password1";

    [Fact]
    public async Task PostStartTotp_Succeeds_WhenJwtHydrateAlreadyTracksUser()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);

        var data = new TestDataBuilder(db);
        var user = data.AddUser("admin", isAdmin: true);
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        data.AddStore("Main", "RUB", "Europe/Moscow", user);
        await data.SaveAsync();
        db.ChangeTracker.Clear();

        // JWT OnTokenValidated → PermissionService.GetUserAccessAsync → FindByIdAsync
        Assert.NotNull(await users.FindByIdAsync(user.Id.ToString()));

        var controller = CreateController(db, users, user.Id);
        var result = await controller.PostStartTotp(new AccountSecurityController.PasswordConfirmForm
        {
            password = Password,
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("otpauth://totp/", json, StringComparison.Ordinal);
        Assert.Contains("\"totpEnabled\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostStartTotp_WhenAlreadyEnabled_ThrowsConflict()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);

        var data = new TestDataBuilder(db);
        var user = data.AddUser("admin", isAdmin: true);
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        await data.SaveAsync();
        db.ChangeTracker.Clear();

        var tracked = await users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(tracked);
        Assert.True((await users.SetTwoFactorEnabledAsync(tracked, true)).Succeeded);

        var controller = CreateController(db, users, user.Id);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            controller.PostStartTotp(new AccountSecurityController.PasswordConfirmForm
            {
                password = Password,
            }));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("totp_already_enabled", ex.Code);
    }

    [Fact]
    public async Task PostConfirmTotp_WhenAlreadyEnabled_ThrowsConflict()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);

        var data = new TestDataBuilder(db);
        var user = data.AddUser("admin", isAdmin: true);
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        await data.SaveAsync();
        db.ChangeTracker.Clear();

        var tracked = await users.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(tracked);
        Assert.True((await users.SetTwoFactorEnabledAsync(tracked, true)).Succeeded);

        var controller = CreateController(db, users, user.Id);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            controller.PostConfirmTotp(new AccountSecurityController.TotpCodeForm { code = "123456" }));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("totp_already_enabled", ex.Code);
    }

    [Fact]
    public async Task PostPasskeyOptions_WithoutPassword_ThrowsUnauthorized()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);

        var data = new TestDataBuilder(db);
        var user = data.AddUser("admin", isAdmin: true);
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, Password);
        await data.SaveAsync();
        db.ChangeTracker.Clear();

        var controller = CreateController(db, users, user.Id);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            controller.PostPasskeyOptions(new AccountSecurityController.PasswordConfirmForm
            {
                password = "",
            }));
        Assert.Equal(401, ex.StatusCode);
    }

    private static AccountSecurityController CreateController(DataContext db, UserManager<User> users, int userId)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("userId", userId.ToString())],
                authenticationType: "test")),
        };
        var current = new CurrentUserService(new HttpContextAccessor { HttpContext = http }, users, db);
        return new AccountSecurityController(users, current, db, new MemoryCache(new MemoryCacheOptions()));
    }

    private static UserManager<User> CreateUserManager(DataContext db)
    {
        var store = new UserStore<User, UserRole, DataContext, int>(db);
        return new UserManager<User>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<User>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            NullLogger<UserManager<User>>.Instance);
    }
}
