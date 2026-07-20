using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public class PublicApiAuthTests
{
    [Fact]
    public void HashKey_IsDeterministicHexSha256()
    {
        var hash1 = ApiKeyAuthenticationHandler.HashKey("dk_test_key_value");
        var hash2 = ApiKeyAuthenticationHandler.HashKey("dk_test_key_value");

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash1);
    }

    [Fact]
    public void HashKey_DifferentInputs_Differ()
    {
        var a = ApiKeyAuthenticationHandler.HashKey("dk_aaa");
        var b = ApiKeyAuthenticationHandler.HashKey("dk_bbb");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ApiException_Forbidden_Is403()
    {
        var ex = ApiException.Forbidden("No access to this store");
        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
    }
}
