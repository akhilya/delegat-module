using System.Text;
using System.Text.Json;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests.Ai;

public class SandboxShopsTests
{
    [Fact]
    public void Wildberries_ProductionHost_WhenFlagSaysProduction()
    {
        var hosts = SandboxShops.Hosts("wildberries", new Dictionary<string, string>
        {
            ["apiKey"] = "live-key",
            ["sandbox"] = "production",
        });
        Assert.Equal("https://content-api.wildberries.ru", hosts["content"]);
        Assert.Equal("https://marketplace-api.wildberries.ru", hosts["marketplace"]);
    }

    [Fact]
    public void Wildberries_SandboxHost_WhenTokenClaimSaysTest()
    {
        var hosts = SandboxShops.Hosts("wildberries", new Dictionary<string, string>
        {
            ["apiKey"] = Jwt("{\"t\":true}"),
            ["sandbox"] = "auto",
        });
        Assert.Equal("https://content-api-sandbox.wildberries.ru", hosts["content"]);
        Assert.Equal("https://discounts-prices-api-sandbox.wildberries.ru", hosts["calendar"]);
    }

    [Fact]
    public void SellerShops_UseThePlatformSnapshot()
    {
        Assert.Equal("yandex", SandboxShops.Docs("yandex")!["platform"]);
        Assert.Equal("yandex", SandboxShops.Docs("yandexmarket")!["platform"]);
        Assert.Equal("wildberries", SandboxShops.Docs("wildberries")!["platform"]);
        Assert.Equal("ozon", SandboxShops.Docs("ozon")!["platform"]);
        Assert.Null(SandboxShops.Docs("opencart"));
        Assert.Null(SandboxShops.Docs("medusa"));
    }

    [Fact]
    public void Ozon_AuthNamesTheSellerHeaders()
    {
        Assert.Contains("Client-Id", SandboxShops.Auth("ozon"), StringComparison.Ordinal);
        var hosts = SandboxShops.Hosts("ozon", new Dictionary<string, string>());
        Assert.Equal("https://api-seller.ozon.ru", hosts["seller"]);
    }

    [Fact]
    public void Redact_StripsApiKeysFromOutput()
    {
        const string key = "wb-secret-key-value";
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                credentials = new Dictionary<string, string>
                {
                    ["apiKey"] = key,
                    ["sandbox"] = "production",
                },
            },
        });
        var secrets = SandboxShops.SecretValues(json);
        var text = SandboxShops.Redact($"brand list ok {key}", secrets);
        Assert.DoesNotContain(key, text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        Assert.Contains("production", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRunBody_PassesShopsThrough()
    {
        using var content = PythonSandboxClient.CreateRunBody(
            "print(1)", 4, "http://api", "Bearer t",
            """[{"id":4,"adapter":"ozon"}]""");
        var json = Encoding.UTF8.GetString(await content.ReadAsByteArrayAsync());
        Assert.Contains("\"adapter\":\"ozon\"", json, StringComparison.Ordinal);
        Assert.Contains("\"storeId\":4", json, StringComparison.Ordinal);
    }

    private static string Jwt(string payloadJson)
    {
        static string B64(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        return B64("{\"alg\":\"none\"}") + "." + B64(payloadJson) + ".sig";
    }
}
