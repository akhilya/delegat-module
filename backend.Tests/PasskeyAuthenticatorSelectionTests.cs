using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Infrastructure.Auth;

namespace Delegat.Tests;

public class PasskeyAuthenticatorSelectionTests
{
    [Fact]
    public void Discoverable_RequiresResidentKeyAndOmitsPlatformAttachment()
    {
        var selection = PasskeyAuthenticatorSelection.Discoverable();

        Assert.Equal("required", selection.ResidentKey);
        Assert.True(selection.RequireResidentKey);
        Assert.Equal("preferred", selection.UserVerification);
        Assert.Null(selection.AuthenticatorAttachment);
    }

    [Fact]
    public void Discoverable_JsonHasNoAuthenticatorAttachment()
    {
        var json = JsonSerializer.Serialize(
            PasskeyAuthenticatorSelection.Discoverable(),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            });

        Assert.Contains("\"residentKey\":\"required\"", json, StringComparison.Ordinal);
        Assert.Contains("\"requireResidentKey\":true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("platform", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authenticatorAttachment", json, StringComparison.Ordinal);
    }
}
