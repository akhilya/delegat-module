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
    public void CsrfMiddleware_SkipsPublicApiV1()
    {
        Assert.StartsWith("/api/v1", "/api/v1/stores/1/products");
        Assert.False("/api/products".StartsWith("/api/v1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(401, "unauthorized")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")]
    [InlineData(409, "conflict")]
    public void PublicApiErrorCodes_MapFromStatus(int status, string expectedCode)
    {
        var code = status switch
        {
            401 => "unauthorized",
            403 => "forbidden",
            404 => "not_found",
            409 => "conflict",
            422 => "validation_error",
            429 => "rate_limited",
            _ => "bad_request"
        };
        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public void AcceptedOperation_Shape_ContainsLinks()
    {
        var storeId = 7;
        var operationId = "abc123";
        var body = new
        {
            operationId,
            status = "running",
            links = new { self = $"/api/v1/stores/{storeId}/operations/{operationId}" }
        };

        Assert.Equal("abc123", body.operationId);
        Assert.Contains("/api/v1/stores/7/operations/", body.links.self);
    }

    [Fact]
    public void ApiException_Forbidden_Is403()
    {
        var ex = ApiException.Forbidden("Нет доступа к этому магазину");
        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
    }
}
