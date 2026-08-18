using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class ConnectionRemoteIdentityTests
{
    [Fact]
    public void Ozon_SameClientId_DifferentApiKeys_Match()
    {
        var a = ConnectionRemoteIdentity.Build(
            "ozon",
            new Dictionary<string, string> { ["clientId"] = "111", ["apiKey"] = "aaa" });
        var b = ConnectionRemoteIdentity.Build(
            "ozon",
            new Dictionary<string, string> { ["clientId"] = "111", ["apiKey"] = "bbb" });

        Assert.Equal(a, b);
    }

    [Fact]
    public void Ozon_DifferentClientIds_DoNotMatch()
    {
        var a = ConnectionRemoteIdentity.Build(
            "ozon",
            new Dictionary<string, string> { ["clientId"] = "111", ["apiKey"] = "aaa" });
        var b = ConnectionRemoteIdentity.Build(
            "ozon",
            new Dictionary<string, string> { ["clientId"] = "222", ["apiKey"] = "aaa" });

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Wildberries_SameApiKey_Match()
    {
        var a = ConnectionRemoteIdentity.Build(
            "wildberries",
            new Dictionary<string, string> { ["apiKey"] = "tok" });
        var b = ConnectionRemoteIdentity.Build(
            "wildberries",
            new Dictionary<string, string> { ["apiKey"] = "tok" });

        Assert.Equal(a, b);
    }

    [Fact]
    public void Wildberries_DifferentApiKeys_DoNotMatch()
    {
        var a = ConnectionRemoteIdentity.Build(
            "wildberries",
            new Dictionary<string, string> { ["apiKey"] = "one" });
        var b = ConnectionRemoteIdentity.Build(
            "wildberries",
            new Dictionary<string, string> { ["apiKey"] = "two" });

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Woo_UrlTrailingSlash_Match()
    {
        var a = ConnectionRemoteIdentity.Build(
            "woocommerce",
            new Dictionary<string, string>
            {
                ["apiBaseUrl"] = "https://Shop.example.com/wp-json/delegat/v1/",
                ["apiKey"] = "a",
                ["bulkChunkSize"] = "50",
            });
        var b = ConnectionRemoteIdentity.Build(
            "woocommerce",
            new Dictionary<string, string>
            {
                ["apiBaseUrl"] = "https://shop.example.com/wp-json/delegat/v1",
                ["apiKey"] = "b",
            });

        Assert.Equal(a, b);
    }

    [Fact]
    public void OpenCart_SameUrl_DifferentStoreId_DoNotMatch()
    {
        var a = ConnectionRemoteIdentity.Build(
            "opencart",
            new Dictionary<string, string>
            {
                ["apiBaseUrl"] = "https://shop.example.com/index.php?route=extension/delegat/api",
                ["storeId"] = "0",
                ["apiKey"] = "a",
            });
        var b = ConnectionRemoteIdentity.Build(
            "opencart",
            new Dictionary<string, string>
            {
                ["apiBaseUrl"] = "https://shop.example.com/index.php?route=extension/delegat/api",
                ["storeId"] = "2",
                ["apiKey"] = "a",
            });

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Yandex_CampaignIdFromDetails_MatchesCredentials()
    {
        var fromDetails = ConnectionRemoteIdentity.Build(
            "yandex",
            new Dictionary<string, string> { ["apiKey"] = "tok" },
            new Dictionary<string, object> { ["campaignId"] = 77L, ["businessId"] = 1L });
        var fromCreds = ConnectionRemoteIdentity.Build(
            "yandex",
            new Dictionary<string, string>
            {
                ["apiKey"] = "other",
                ["campaignId"] = "77",
            });

        Assert.Equal(fromDetails, fromCreds);
    }

    [Fact]
    public void DifferentAdapters_DoNotMatch()
    {
        var creds = new Dictionary<string, string> { ["apiKey"] = "same" };
        Assert.NotEqual(
            ConnectionRemoteIdentity.Build("ozon", creds),
            ConnectionRemoteIdentity.Build("wildberries", creds));
    }

    [Fact]
    public void OpenCart_MissingStoreId_DefaultsToZero()
    {
        var a = ConnectionRemoteIdentity.Build(
            "opencart",
            new Dictionary<string, string>
            {
                ["apiBaseUrl"] = "https://shop.example.com/index.php?route=extension/delegat/api",
                ["apiKey"] = "a",
            });
        var b = ConnectionRemoteIdentity.Build(
            "opencart",
            new Dictionary<string, string>
            {
                ["apiBaseUrl"] = "https://shop.example.com/index.php?route=extension/delegat/api",
                ["storeId"] = "0",
                ["apiKey"] = "b",
            },
            new Dictionary<string, object> { ["storeId"] = 0 });

        Assert.Equal(a, b);
    }
}
