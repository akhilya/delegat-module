using Delegat.Infrastructure.Security;
using Delegat.Services.Ai;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Tests;

public class AiMcpServerServiceTests
{
    [Fact]
    public async Task CreateUpdateToggleAndDelete_PersistsEncryptedKey()
    {
        await using var db = TestDbFactory.CreateDb();
        var service = CreateService(db);

        var created = await service.CreateAsync("Docs", "https://mcp.example.test/mcp", "sk-secret");
        Assert.True(created.isEnabled);
        Assert.StartsWith("gcm:", created.apiKeyEncrypted);

        await service.UpdateAsync(created, "Docs 2", "https://other.example.test/mcp", null);
        var saved = await db.aiMcpServers.SingleAsync();
        Assert.Equal("Docs 2", saved.name);
        Assert.Equal("https://other.example.test/mcp", saved.url);
        Assert.Equal("sk-secret", service.DecryptApiKey(saved));

        await service.ToggleEnabledAsync(saved.Id);
        Assert.False((await db.aiMcpServers.SingleAsync()).isEnabled);

        await service.DeleteAsync(await db.aiMcpServers.SingleAsync());
        Assert.Empty(await db.aiMcpServers.ToListAsync());
    }

    [Fact]
    public async Task Create_RejectsNonHttpUrl()
    {
        await using var db = TestDbFactory.CreateDb();
        var service = CreateService(db);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("Bad", "ftp://mcp.example.test", null));
    }

    private static AiMcpServerService CreateService(DataContext db)
    {
        var key = Convert.ToBase64String(new byte[32]);
        return new AiMcpServerService(db, new EncryptionService(key));
    }
}
