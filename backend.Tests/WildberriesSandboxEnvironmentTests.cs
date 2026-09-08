using System.Text;
using System.Text.Json;
using Delegat.Marketplace.Wildberries;

namespace Delegat.Tests;

public class WildberriesSandboxEnvironmentTests : IDisposable
{
    public WildberriesSandboxEnvironmentTests()
    {
        WildberriesApiEnvironment.ClearLearnedSandboxTokens();
    }

    public void Dispose()
    {
        WildberriesApiEnvironment.ClearLearnedSandboxTokens();
    }

    [Fact]
    public void UseSandbox_AutoCredential_FollowsJwt()
    {
        var token = Jwt(new { t = true });

        Assert.True(WildberriesApiEnvironment.UseSandbox(token, "auto"));
    }

    [Fact]
    public void UseSandbox_JwtTTrue_IsSandbox()
    {
        var token = Jwt(new { t = true });

        Assert.True(WildberriesApiEnvironment.UseSandbox(token, sandboxCredential: null));
    }

    [Fact]
    public void UseSandbox_JwtTFalse_IsProduction()
    {
        var token = Jwt(new { t = false });

        Assert.False(WildberriesApiEnvironment.UseSandbox(token, sandboxCredential: null));
    }

    [Fact]
    public void UseSandbox_PlainToken_IsProduction()
    {
        Assert.False(WildberriesApiEnvironment.UseSandbox("not-a-jwt", sandboxCredential: null));
    }

    [Fact]
    public void UseSandbox_ExplicitSandbox_OverridesProductionJwt()
    {
        var token = Jwt(new { t = false });

        Assert.True(WildberriesApiEnvironment.UseSandbox(token, "sandbox"));
    }

    [Fact]
    public void UseSandbox_ExplicitProduction_OverridesSandboxJwt()
    {
        var token = Jwt(new { t = true });

        Assert.False(WildberriesApiEnvironment.UseSandbox(token, "production"));
    }

    [Fact]
    public void UseSandbox_LearnedFromForbidden_WithoutJwtClaim()
    {
        const string token = "opaque-test-token";
        Assert.False(WildberriesApiEnvironment.UseSandbox(token, null));

        WildberriesApiEnvironment.RememberSandboxToken(token);

        Assert.True(WildberriesApiEnvironment.UseSandbox(token, null));
    }

    [Fact]
    public void BaseAddress_MarketplaceSandbox_UsesOfficialHost()
    {
        var uri = WildberriesApiEnvironment.BaseAddress(
            WildberriesApiDomain.Marketplace, sandbox: true);

        Assert.Equal("https://marketplace-api-sandbox.wildberries.ru/", uri.ToString());
    }

    [Fact]
    public void BaseAddress_ContentAndPricesSandbox_UseOfficialHosts()
    {
        Assert.Equal(
            "https://content-api-sandbox.wildberries.ru/",
            WildberriesApiEnvironment.BaseAddress(WildberriesApiDomain.Content, true).ToString());
        Assert.Equal(
            "https://discounts-prices-api-sandbox.wildberries.ru/",
            WildberriesApiEnvironment.BaseAddress(WildberriesApiDomain.Prices, true).ToString());
        Assert.Equal(
            "https://discounts-prices-api-sandbox.wildberries.ru/",
            WildberriesApiEnvironment.BaseAddress(WildberriesApiDomain.Calendar, true).ToString());
    }

    [Fact]
    public void BaseAddress_Production_KeepsCurrentHosts()
    {
        Assert.Equal(
            "https://marketplace-api.wildberries.ru/",
            WildberriesApiEnvironment.BaseAddress(WildberriesApiDomain.Marketplace, false).ToString());
        Assert.Equal(
            "https://dp-calendar-api.wildberries.ru/",
            WildberriesApiEnvironment.BaseAddress(WildberriesApiDomain.Calendar, false).ToString());
    }

    [Fact]
    public void IsSandboxTokenOnProductionError_MatchesWbDetail()
    {
        const string body =
            """{"status":403,"detail":"scope is not allowed for this resource, sandbox token used for production endpoint"}""";

        Assert.True(WildberriesApiEnvironment.IsSandboxTokenOnProductionError(body));
        Assert.False(WildberriesApiEnvironment.IsSandboxTokenOnProductionError("""{"status":403,"detail":"forbidden"}"""));
    }

    [Fact]
    public void PluginSchema_IncludesSandboxSelect()
    {
        var sandbox = new WildberriesPlugin().GetCredentialsSchema().Fields
            .Single(f => f.Key == "sandbox");

        Assert.Equal("select", sandbox.Type);
        Assert.False(sandbox.IsSecret);
        Assert.Equal("auto", sandbox.DefaultValue);
        Assert.Contains(sandbox.Options!, o => o.Value == "sandbox");
        Assert.Contains(sandbox.Options!, o => o.Value == "production");
    }

    private static string Jwt(object payload)
    {
        static string B64Url(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        var header = B64Url("""{"alg":"ES256","typ":"JWT"}""");
        var body = B64Url(JsonSerializer.Serialize(payload));
        return $"{header}.{body}.sig";
    }
}
