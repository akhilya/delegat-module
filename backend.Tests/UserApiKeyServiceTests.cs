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
}
