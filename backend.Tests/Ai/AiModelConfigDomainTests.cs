using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AiModelConfigDomainTests
{
    [Fact]
    public async Task CreateUpdateAndDelete_PersistsAllEditableModelFields()
    {
        await using var db = TestDbFactory.CreateDb();
        var service = CreateService(db);

        var created = await service.CreateAsync(
            "Primary", "https://api.example.test", "secret", "model-1",
            AiModelProvider.OpenAiCompatible, " https://proxy.example.test ");

        await service.UpdateAsync(
            created, "Renamed", "https://new.example.test", "new-secret", "model-2",
            AiModelProvider.Anthropic, " ");

        var saved = await db.aiModelConfigs.SingleAsync();
        Assert.Equal("Renamed", saved.name);
        Assert.Equal("https://new.example.test", saved.apiUrl);
        Assert.Equal("new-secret", service.DecryptForUse(saved).apiKeyEncrypted);
        Assert.StartsWith("gcm:", saved.apiKeyEncrypted);
        Assert.Equal("model-2", saved.modelId);
        Assert.Equal(AiModelProvider.Anthropic, saved.provider);
        Assert.Null(saved.proxyUrl);
        Assert.True(saved.isDefault);
        Assert.True(saved.isEnabled);

        await service.DeleteAsync(saved);

        Assert.Empty(await db.aiModelConfigs.ToListAsync());
    }

    [Fact]
    public void DecryptForUse_AcceptsLegacyPlaintextApiKey()
    {
        // Older rows stored the key in plaintext. Decrypt must not throw until migration re-encrypts.
        var service = CreateService(TestDbFactory.CreateDb());
        var model = new AiModelConfig
        {
            name = "Legacy",
            apiUrl = "https://api.example.test",
            apiKeyEncrypted = "sk-legacy-plaintext-key",
            modelId = "model-1",
            provider = AiModelProvider.OpenAiCompatible,
        };

        var decrypted = service.DecryptForUse(model);

        Assert.Equal("sk-legacy-plaintext-key", decrypted.apiKeyEncrypted);
        Assert.Equal(model.name, decrypted.name);
        Assert.Equal(model.modelId, decrypted.modelId);
    }

    [Fact]
    public void DecryptForUse_CorruptCiphertext_ThrowsAiApiKeyInvalid()
    {
        var service = CreateService(TestDbFactory.CreateDb());
        // Looks like gcm ciphertext (valid Base64, long enough) but wrong auth tag / key material
        var corrupt = "gcm:" + Convert.ToBase64String(Enumerable.Range(0, 40).Select(i => (byte)i).ToArray());
        var model = new AiModelConfig
        {
            name = "Broken",
            apiUrl = "https://api.example.test",
            apiKeyEncrypted = corrupt,
            modelId = "model-1",
            provider = AiModelProvider.OpenAiCompatible,
        };

        var ex = Assert.Throws<ApiException>(() => service.DecryptForUse(model));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("ai_api_key_invalid", ex.Code);
        Assert.Contains("could not be decrypted", ex.Message);
    }

    [Fact]
    public async Task SetDefaultAsync_LeavesExactlyOneDefaultModel()
    {
        await using var db = TestDbFactory.CreateDb();
        var service = CreateService(db);
        var first = await service.CreateAsync(
            "First", "https://one.example.test", "key-1", "model-1", AiModelProvider.Anthropic);
        var second = await service.CreateAsync(
            "Second", "https://two.example.test", "key-2", "model-2", AiModelProvider.OpenAiCompatible);

        await service.SetDefaultAsync(second.Id);

        var defaults = await db.aiModelConfigs.Where(model => model.isDefault).ToListAsync();
        Assert.Single(defaults);
        Assert.Equal(second.Id, defaults[0].Id);
        Assert.False((await db.aiModelConfigs.FindAsync(first.Id))!.isDefault);
    }

    [Fact]
    public async Task DeleteDefault_AssignsRemainingModelAsDefault()
    {
        await using var db = TestDbFactory.CreateDb();
        var service = CreateService(db);
        var first = await service.CreateAsync(
            "First", "https://one.example.test", "key-1", "model-1", AiModelProvider.Anthropic);
        var second = await service.CreateAsync(
            "Second", "https://two.example.test", "key-2", "model-2", AiModelProvider.OpenAiCompatible);

        await service.DeleteAsync(first);

        var remaining = await db.aiModelConfigs.SingleAsync();
        Assert.Equal(second.Id, remaining.Id);
        Assert.True(remaining.isDefault);
    }

    private static AiModelConfigService CreateService(DataContext db)
    {
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        return new(db, new AiApiClient(new HttpClient(), NullLogger<AiApiClient>.Instance), new EncryptionService(key));
    }
}
