using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class UserApiKeyServiceTests
{
    private static DataContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<DataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DataContext(options);
    }

    [Fact]
    public async Task CreateAsync_StoresHashAndReturnsPlaintextOnce()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = 1,
            UserName = "tester",
            Email = "t@example.com",
            NormalizedUserName = "TESTER",
            NormalizedEmail = "T@EXAMPLE.COM",
            isActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        var svc = new UserApiKeyService(db);
        var (dto, plaintext) = await svc.CreateAsync(1, "ERP");

        Assert.StartsWith("dk_", plaintext);
        Assert.Equal(1, await db.userApiKeys.CountAsync());

        var stored = await db.userApiKeys.SingleAsync();
        Assert.Equal(ApiKeyAuthenticationHandler.HashKey(plaintext), stored.keyHash);
        Assert.Equal(plaintext[^4..], stored.last4);
        Assert.Null(stored.revokedAt);

        var list = await svc.ListAsync(1);
        Assert.Single(list);
    }

    [Fact]
    public async Task RevokeAsync_SetsRevokedAt()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = 2,
            UserName = "u2",
            Email = "u2@example.com",
            NormalizedUserName = "U2",
            NormalizedEmail = "U2@EXAMPLE.COM",
            isActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        var svc = new UserApiKeyService(db);
        var (_, _) = await svc.CreateAsync(2, "n8n");
        var key = await db.userApiKeys.SingleAsync();

        await svc.RevokeAsync(2, key.Id);
        Assert.NotNull((await db.userApiKeys.SingleAsync()).revokedAt);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_Throws()
    {
        await using var db = CreateDb();
        var svc = new UserApiKeyService(db);
        await Assert.ThrowsAsync<ApiException>(() => svc.CreateAsync(1, "  "));
    }

    [Fact]
    public async Task CreateAsync_TrimsName()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = 30,
            UserName = "u30",
            Email = "u30@example.com",
            NormalizedUserName = "U30",
            NormalizedEmail = "U30@EXAMPLE.COM",
            isActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        var svc = new UserApiKeyService(db);
        var (dto, _) = await svc.CreateAsync(30, "  ERP bridge  ");
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("ERP bridge", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateAsync_NameTooLong_Throws()
    {
        await using var db = CreateDb();
        var svc = new UserApiKeyService(db);
        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.CreateAsync(1, new string('x', 101)));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task RevokeAsync_MissingKey_ThrowsNotFound()
    {
        await using var db = CreateDb();
        var svc = new UserApiKeyService(db);
        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.RevokeAsync(1, 999));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task RevokeAsync_OtherUsersKey_ThrowsNotFound()
    {
        await using var db = CreateDb();
        db.Users.AddRange(
            new User
            {
                Id = 10,
                UserName = "a",
                Email = "a@example.com",
                NormalizedUserName = "A",
                NormalizedEmail = "A@EXAMPLE.COM",
                isActive = true,
                SecurityStamp = Guid.NewGuid().ToString()
            },
            new User
            {
                Id = 11,
                UserName = "b",
                Email = "b@example.com",
                NormalizedUserName = "B",
                NormalizedEmail = "B@EXAMPLE.COM",
                isActive = true,
                SecurityStamp = Guid.NewGuid().ToString()
            });
        await db.SaveChangesAsync();

        var svc = new UserApiKeyService(db);
        var (_, _) = await svc.CreateAsync(10, "owner-key");
        var key = await db.userApiKeys.SingleAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.RevokeAsync(11, key.Id));
        Assert.Equal(404, ex.StatusCode);
        Assert.Null((await db.userApiKeys.SingleAsync()).revokedAt);
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_IsIdempotent()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Id = 12,
            UserName = "c",
            Email = "c@example.com",
            NormalizedUserName = "C",
            NormalizedEmail = "C@EXAMPLE.COM",
            isActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await db.SaveChangesAsync();

        var svc = new UserApiKeyService(db);
        await svc.CreateAsync(12, "once");
        var key = await db.userApiKeys.SingleAsync();
        await svc.RevokeAsync(12, key.Id);
        var firstRevokedAt = (await db.userApiKeys.SingleAsync()).revokedAt;

        await svc.RevokeAsync(12, key.Id);
        Assert.Equal(firstRevokedAt, (await db.userApiKeys.SingleAsync()).revokedAt);
    }

    [Fact]
    public async Task ListAsync_OnlyReturnsCallerKeys()
    {
        await using var db = CreateDb();
        db.Users.AddRange(
            new User
            {
                Id = 20,
                UserName = "u20",
                Email = "u20@example.com",
                NormalizedUserName = "U20",
                NormalizedEmail = "U20@EXAMPLE.COM",
                isActive = true,
                SecurityStamp = Guid.NewGuid().ToString()
            },
            new User
            {
                Id = 21,
                UserName = "u21",
                Email = "u21@example.com",
                NormalizedUserName = "U21",
                NormalizedEmail = "U21@EXAMPLE.COM",
                isActive = true,
                SecurityStamp = Guid.NewGuid().ToString()
            });
        await db.SaveChangesAsync();

        var svc = new UserApiKeyService(db);
        await svc.CreateAsync(20, "mine");
        await svc.CreateAsync(21, "theirs");

        var mine = await svc.ListAsync(20);
        Assert.Single(mine);
    }
}
