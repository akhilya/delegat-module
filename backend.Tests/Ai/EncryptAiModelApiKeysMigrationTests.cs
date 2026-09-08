using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;
using Delegat.Services.DataMigrations;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

/// <summary>
/// Data migration 1.5.0 must encrypt leftover plaintext AI keys without
/// re-encrypting values that are already ciphertext.
/// </summary>
public class EncryptAiModelApiKeysMigrationTests
{
    [Fact]
    public async Task Execute_EncryptsPlaintextKeys_LeavesCiphertextUntouched()
    {
        await using var db = TestDbFactory.CreateDb();
        var encryption = CreateEncryption();

        var alreadyEncrypted = encryption.Encrypt("already-secret");
        db.aiModelConfigs.AddRange(
            new AiModelConfig
            {
                name = "Plain",
                apiUrl = "https://api.example.test",
                apiKeyEncrypted = "sk-needs-encryption",
                modelId = "m1",
                provider = AiModelProvider.OpenAiCompatible,
            },
            new AiModelConfig
            {
                name = "Cipher",
                apiUrl = "https://api.example.test",
                apiKeyEncrypted = alreadyEncrypted,
                modelId = "m2",
                provider = AiModelProvider.OpenAiCompatible,
            },
            new AiModelConfig
            {
                name = "Empty",
                apiUrl = "https://api.example.test",
                apiKeyEncrypted = "",
                modelId = "m3",
                provider = AiModelProvider.OpenAiCompatible,
            });
        await db.SaveChangesAsync();

        var services = new ServiceCollection()
            .AddSingleton(encryption)
            .AddSingleton(db)
            .BuildServiceProvider();

        await new Migration_1_5_0_EncryptAiModelApiKeys().ExecuteAsync(services, CancellationToken.None);

        var plain = await db.aiModelConfigs.SingleAsync(m => m.name == "Plain");
        var cipher = await db.aiModelConfigs.SingleAsync(m => m.name == "Cipher");
        var empty = await db.aiModelConfigs.SingleAsync(m => m.name == "Empty");

        Assert.True(encryption.IsCiphertext(plain.apiKeyEncrypted));
        Assert.Equal("sk-needs-encryption", encryption.Decrypt(plain.apiKeyEncrypted));
        Assert.Equal(alreadyEncrypted, cipher.apiKeyEncrypted);
        Assert.Equal("", empty.apiKeyEncrypted);
    }

    [Fact]
    public async Task Execute_WhenEncryptionServiceMissing_IsNoOp()
    {
        await using var db = TestDbFactory.CreateDb();
        db.aiModelConfigs.Add(new AiModelConfig
        {
            name = "Plain",
            apiUrl = "https://api.example.test",
            apiKeyEncrypted = "still-plain",
            modelId = "m1",
            provider = AiModelProvider.OpenAiCompatible,
        });
        await db.SaveChangesAsync();

        var services = new ServiceCollection()
            .AddSingleton(db)
            .BuildServiceProvider();

        await new Migration_1_5_0_EncryptAiModelApiKeys().ExecuteAsync(services, CancellationToken.None);

        var saved = await db.aiModelConfigs.SingleAsync();
        Assert.Equal("still-plain", saved.apiKeyEncrypted);
    }

    private static EncryptionService CreateEncryption()
    {
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        return new EncryptionService(key);
    }
}
