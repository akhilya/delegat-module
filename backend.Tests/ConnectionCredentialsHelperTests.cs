using Delegat.Infrastructure.Security;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class ConnectionCredentialsHelperTests
{
    private static EncryptionService CreateEncryption()
    {
        // 32 zero bytes, base64
        var key = Convert.ToBase64String(new byte[32]);
        return new EncryptionService(key);
    }

    [Fact]
    public void ExtractPublicCredentials_OmitsSecrets()
    {
        var encryption = CreateEncryption();
        var plain = """{"apiKey":"secret","businessId":"42","campaignId":"7"}""";
        var encrypted = encryption.Encrypt(plain);
        var schema = new CredentialsSchema
        {
            Fields =
            {
                new CredentialField { Key = "apiKey", IsSecret = true },
                new CredentialField { Key = "businessId", IsSecret = false },
                new CredentialField { Key = "campaignId", IsSecret = false },
            }
        };

        var pub = ConnectionCredentialsHelper.ExtractPublicCredentials(encrypted, schema, encryption);

        Assert.Equal("42", pub["businessId"]);
        Assert.Equal("7", pub["campaignId"]);
        Assert.False(pub.ContainsKey("apiKey"));
    }

    [Fact]
    public void MergeAndEncrypt_KeepsEmptySecretFromExisting()
    {
        var encryption = CreateEncryption();
        var existing = encryption.Encrypt("""{"apiKey":"keep-me","businessId":"1"}""");
        var schema = new CredentialsSchema
        {
            Fields =
            {
                new CredentialField { Key = "apiKey", IsSecret = true },
                new CredentialField { Key = "businessId", IsSecret = false },
            }
        };

        var mergedEncrypted = ConnectionCredentialsHelper.MergeAndEncrypt(
            existing,
            """{"apiKey":"","businessId":"99"}""",
            schema,
            encryption);

        var merged = ConnectionCredentialsHelper.ParseCredentialsObject(
            ConnectionCredentialsHelper.DecryptCredentialsJson(mergedEncrypted, encryption));

        Assert.Equal("keep-me", merged["apiKey"]);
        Assert.Equal("99", merged["businessId"]);
    }

    [Fact]
    public void TryPersistMissingPublicFieldsFromDetails_WritesBusinessId()
    {
        var encryption = CreateEncryption();
        var connection = new Models.Marketplace.MarketplaceConnection
        {
            name = "y",
            adapterKey = "yandex",
            credentialsJson = encryption.Encrypt("""{"apiKey":"tok"}"""),
            storeId = 1,
        };
        var schema = new CredentialsSchema
        {
            Fields =
            {
                new CredentialField { Key = "apiKey", IsSecret = true },
                new CredentialField { Key = "businessId", IsSecret = false },
            }
        };

        var changed = ConnectionCredentialsHelper.TryPersistMissingPublicFieldsFromDetails(
            connection,
            new Dictionary<string, object> { ["businessId"] = 12345L },
            schema,
            encryption,
            "businessId", "campaignId");

        Assert.True(changed);
        var pub = ConnectionCredentialsHelper.ExtractPublicCredentials(
            connection.credentialsJson, schema, encryption);
        Assert.Equal("12345", pub["businessId"]);
    }
}
