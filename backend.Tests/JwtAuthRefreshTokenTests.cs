using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Delegat.Tests;

public class JwtAuthRefreshTokenTests
{
    [Fact]
    public async Task ActiveToken_RotatesAndLinksReplacement()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);
        var userId = await AddUserAsync(db);
        await AddTokenAsync(db, userId, "old");

        var (controller, http) = CreateController(db, users, "old");
        Assert.IsType<OkResult>(await controller.PostRefresh());

        var issued = ReadRefreshCookie(http);
        Assert.NotNull(issued);
        Assert.NotEqual("old", issued);
        var old = await db.refreshTokens.AsNoTracking().SingleAsync(rt => rt.token == "old");
        var replacement = await db.refreshTokens.AsNoTracking().SingleAsync(rt => rt.token == issued);
        Assert.NotNull(old.revokedAt);
        Assert.Equal(replacement.Id, old.replacedByTokenId);
        Assert.True(replacement.IsActive);
    }

    [Fact]
    public async Task RotatedToken_WithinGrace_ReturnsOnlyItsReplacement()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);
        var userId = await AddUserAsync(db);
        await AddTokenAsync(db, userId, "other-device");
        var replacement = await AddTokenAsync(db, userId, "replacement");
        await AddTokenAsync(db, userId, "rotated",
            revokedAt: DateTime.UtcNow.AddSeconds(-5), replacedByTokenId: replacement.Id);

        var (controller, http) = CreateController(db, users, "rotated");
        Assert.IsType<OkResult>(await controller.PostRefresh());

        Assert.Equal("replacement", ReadRefreshCookie(http));
        Assert.Equal(2, await db.refreshTokens.CountAsync(rt => rt.revokedAt == null));
    }

    [Fact]
    public async Task LogoutRevokedToken_IsRejectedAndRevokesUserSessions()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);
        var userId = await AddUserAsync(db);
        await AddTokenAsync(db, userId, "logged-out", revokedAt: DateTime.UtcNow.AddSeconds(-1));
        await AddTokenAsync(db, userId, "new-session");

        var (controller, _) = CreateController(db, users, "logged-out");
        var ex = await Assert.ThrowsAsync<ApiException>(() => controller.PostRefresh());

        Assert.Equal(StatusCodes.Status401Unauthorized, ex.StatusCode);
        Assert.False(await db.refreshTokens.AnyAsync(rt => rt.revokedAt == null));
    }

    [Fact]
    public async Task RotatedToken_AfterGrace_IsTreatedAsReuse()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);
        var userId = await AddUserAsync(db);
        var replacement = await AddTokenAsync(db, userId, "replacement");
        await AddTokenAsync(db, userId, "rotated",
            revokedAt: DateTime.UtcNow - RefreshToken.RotationGracePeriod - TimeSpan.FromSeconds(1),
            replacedByTokenId: replacement.Id);

        var (controller, http) = CreateController(db, users, "rotated");
        await Assert.ThrowsAsync<ApiException>(() => controller.PostRefresh());

        Assert.Null(ReadRefreshCookie(http));
        Assert.False(await db.refreshTokens.AnyAsync(rt => rt.revokedAt == null));
    }

    [Fact]
    public async Task RotatedToken_WithinGrace_WhenReplacementRevoked_IsRejected()
    {
        await using var db = TestDbFactory.CreateDb();
        using var users = CreateUserManager(db);
        var userId = await AddUserAsync(db);
        var replacement = await AddTokenAsync(db, userId, "replacement", revokedAt: DateTime.UtcNow);
        await AddTokenAsync(db, userId, "rotated",
            revokedAt: DateTime.UtcNow.AddSeconds(-5), replacedByTokenId: replacement.Id);
        await AddTokenAsync(db, userId, "other-device");

        var (controller, _) = CreateController(db, users, "rotated");
        await Assert.ThrowsAsync<ApiException>(() => controller.PostRefresh());

        Assert.False(await db.refreshTokens.AnyAsync(rt => rt.revokedAt == null));
    }

    private static async Task<int> AddUserAsync(DataContext db)
    {
        var data = new TestDataBuilder(db);
        var user = data.AddUser("alice");
        await data.SaveAsync();
        db.ChangeTracker.Clear();
        return user.Id;
    }

    private static async Task<RefreshToken> AddTokenAsync(
        DataContext db, int userId, string token, DateTime? revokedAt = null, int? replacedByTokenId = null)
    {
        var row = new RefreshToken
        {
            userId = userId,
            token = token,
            createdAt = DateTime.UtcNow.AddMinutes(-10),
            expiresAt = DateTime.UtcNow.AddDays(7),
            revokedAt = revokedAt,
            replacedByTokenId = replacedByTokenId,
        };
        db.refreshTokens.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return row;
    }

    private static (JwtAuthController controller, DefaultHttpContext http) CreateController(
        DataContext db, UserManager<User> users, string refreshCookie)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"refresh_token={refreshCookie}";
        var jwt = new JwtService(new JwtSettings
        {
            Key = "test-secret-key-that-is-long-enough-32b!",
            Issuer = "delegat-test-issuer",
            Audience = "delegat-test-audience",
        });
        var controller = new JwtAuthController(
            users, null!, jwt, TestDbFactory.CreateUnitOfWork(db), null!, null!, null!, null!, db)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return (controller, http);
    }

    private static string? ReadRefreshCookie(DefaultHttpContext http)
    {
        foreach (var header in http.Response.Headers.SetCookie)
        {
            if (header != null && header.StartsWith("refresh_token=", StringComparison.Ordinal))
            {
                var value = header["refresh_token=".Length..].Split(';')[0];
                return string.IsNullOrEmpty(value) ? null : Uri.UnescapeDataString(value);
            }
        }
        return null;
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
