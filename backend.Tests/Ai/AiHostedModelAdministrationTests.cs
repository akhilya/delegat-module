using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Delegat.Controllers;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.PlatformLink;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AiHostedModelAdministrationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModelList_IncludesProvidedModelWithoutCredentials_EvenWhenAllowanceIsUnavailable(bool available)
    {
        await using var db = TestDbFactory.CreateDb();
        var controller = CreateController(db, Config(available));
        db.aiModelConfigs.Add(new AiModelConfig
        {
            name = "Local model", modelId = "local-model", apiUrl = "https://local.test",
            apiKeyEncrypted = "local-secret-key", isEnabled = false,
        });
        await db.SaveChangesAsync();

        var result = Json(Assert.IsType<OkObjectResult>(await controller.GetAll(default)));

        Assert.Equal(2, result.GetArrayLength());
        var provided = result.EnumerateArray().Single(model => model.GetProperty("isPlatformHosted").GetBoolean());
        Assert.Equal(AiModelConfigService.PlatformHostedModelId, provided.GetProperty("id").GetInt32());
        Assert.Equal("deepseek-flash", provided.GetProperty("modelId").GetString());
        Assert.Equal(16384, provided.GetProperty("maxTokens").GetInt32());
        Assert.Equal(JsonValueKind.Null, provided.GetProperty("apiKeyMasked").ValueKind);
        Assert.DoesNotContain("local-secret-key", result.GetRawText());
        Assert.DoesNotContain("apiKeyEncrypted", result.GetRawText());
        Assert.Single(await db.aiModelConfigs.ToListAsync());
    }

    [Fact]
    public async Task AdminAndChatUsage_ReportOnlyPercentageAndAvailabilityWithoutTokenCounts()
    {
        await using var db = TestDbFactory.CreateDb();
        var controller = CreateController(db, Config(true));

        var admin = Json(Assert.IsType<OkObjectResult>(await controller.GetHostedAiAdminStatus(default)));
        var chat = Json(Assert.IsType<OkObjectResult>(await controller.GetHostedAiStatus(default)));

        Assert.Equal(chat.GetRawText(), admin.GetRawText());
        Assert.Equal(1.2, admin.GetProperty("usagePercent").GetDouble());
        Assert.False(admin.GetProperty("quotaExhausted").GetBoolean());
        Assert.Equal(new[] { "available", "configured", "entitled", "quotaExhausted", "usagePercent", "usageUnavailable" },
            admin.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Theory]
    [InlineData(0, 100, 0, false)]
    [InlineData(100, 110, 100, true)]
    [InlineData(100, -5, 0, false)]
    [InlineData(100, 50, 50, false)]
    public async Task UsagePercentage_StaysBoundedAndDoesNotTreatReservationsAsSpent(
        long quota, long used, double expected, bool exhausted)
    {
        await using var db = TestDbFactory.CreateDb();
        var config = Config(true);
        config.QuotaTokens = quota;
        config.UsedTokens = used;
        config.ReservedTokens = Math.Max(0, quota - used);
        var controller = CreateController(db, config);

        var result = Json(Assert.IsType<OkObjectResult>(await controller.GetHostedAiAdminStatus(default)));

        Assert.Equal(expected, result.GetProperty("usagePercent").GetDouble());
        Assert.Equal(exhausted, result.GetProperty("quotaExhausted").GetBoolean());
    }

    [Fact]
    public async Task ProvidedModel_CannotBeMutatedByInstanceAdministration()
    {
        await using var db = TestDbFactory.CreateDb();
        var controller = CreateController(db, Config(true));
        db.aiModelConfigs.Add(new AiModelConfig
        {
            name = "Default", modelId = "local", apiUrl = "https://local.test",
            apiKeyEncrypted = "key", isDefault = true,
        });
        await db.SaveChangesAsync();
        var id = AiModelConfigService.PlatformHostedModelId;

        Assert.IsType<NotFoundResult>(await controller.Delete(id, default));
        Assert.IsType<NotFoundResult>(await controller.SetDefault(id, default));
        Assert.IsType<NotFoundResult>(await controller.ToggleEnabled(id, default));
        Assert.True((await db.aiModelConfigs.SingleAsync()).isDefault);
    }

    private static JsonElement Json(OkObjectResult result) =>
        JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static PlatformHostedAiConfigDto Config(bool available) => new()
    {
        Configured = true, Entitled = true, Available = available,
        Provider = "Anthropic", ApiUrl = "https://cp.test/platform/v1/ai/anthropic",
        ModelId = "deepseek-flash", MaxOutputTokens = 16384,
        QuotaTokens = 2000000, UsedTokens = 24221, RemainingTokens = 1975779,
        PeriodEnd = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static AiSettingsController CreateController(DataContext db, PlatformHostedAiConfigDto config)
    {
        var client = new PlatformLinkClient(new HttpClient(new ConfigHandler(config))
        {
            BaseAddress = new Uri("https://cp.test"),
        }, new PlatformLinkOptions
        {
            ControlPlaneUrl = "https://cp.test",
            InstanceId = "inst_0123456789abcdef0123456789abcdef",
            InstanceLinkSecret = "test-link-secret",
        }, NullLogger<PlatformLinkClient>.Instance);
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var service = new AiModelConfigService(db,
            new AiApiClient(new HttpClient(), NullLogger<AiApiClient>.Instance), new EncryptionService(key), client);
        return new AiSettingsController(null!, service, null!, null!, db);
    }

    private sealed class ConfigHandler(PlatformHostedAiConfigDto config) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(config) });
    }
}
