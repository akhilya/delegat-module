using Delegat.Marketplace.SDK;

namespace Delegat.Tests;

public class MarketplaceApiErrorTextTests
{
    [Fact]
    public void Wildberries_MissingContentScope_NamesTheRight()
    {
        var body = """{"title":"unauthorized","detail":"token scope content is required","code":"accessDenied","origin":"content","status":401}""";
        var text = MarketplaceApiErrorText.Format("Wildberries", 401, "WB API error: Unauthorized: " + body, body);
        Assert.Contains("Content (product cards)", text);
        Assert.DoesNotContain("{", text);
        Assert.Contains("token", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ozon_Forbidden_DoesNotDumpJson()
    {
        var body = """{"code":7,"message":"User doesn't have permission to the method","details":[]}""";
        var text = MarketplaceApiErrorText.Format("Ozon", 403, "Ozon API error: Forbidden: " + body, body);
        Assert.DoesNotContain("{", text);
        Assert.Contains("permission", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ozon", text);
    }

    [Fact]
    public void Yandex_ErrorsArray_UsesMessage()
    {
        var body = """{"errors":[{"code":"FORBIDDEN","message":"Access to campaign 123 is denied"}]}""";
        var text = MarketplaceApiErrorText.Format("Yandex Market", 403, body, body);
        Assert.Contains("campaign 123", text);
        Assert.DoesNotContain("\"code\"", text);
    }

    [Fact]
    public void InvalidKey_401_TellsToCheckCredentials()
    {
        var text = MarketplaceApiErrorText.Format("Ozon", 401, "Unauthorized", """{"message":"Invalid Api-Key"}""");
        Assert.Contains("invalid", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("credentials", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RateLimit_IsPlainLanguage()
    {
        var text = MarketplaceApiErrorText.Format("Wildberries", 429, "WB API rate limited", null);
        Assert.Contains("too many requests", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{", text);
    }

    [Fact]
    public void BridgeObjectError_UsesMessage()
    {
        var text = MarketplaceApiErrorText.Format(
            "Medusa",
            500,
            "failed",
            """{"success":false,"error":{"message":"Category pcat_1 was not found"}}""");
        Assert.Contains("Category pcat_1", text);
        Assert.DoesNotContain("success", text);
    }
}
