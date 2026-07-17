using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Delegat.Tests;

public class JwtServiceTests
{
    private static JwtService CreateService() =>
        new(new JwtSettings
        {
            Key = "test-secret-key-that-is-long-enough-32b!",
            Issuer = "delegat-test",
            Audience = "delegat-test-audience"
        });

    [Fact]
    public void GenerateToken_ContainsUserClaims()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(42, "alice");
        var principal = svc.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.Equal("42", principal!.FindFirst("userId")?.Value);
        Assert.Equal("alice", principal.FindFirst(JwtRegisteredClaimNames.Name)?.Value
            ?? principal.FindFirst(ClaimTypes.Name)?.Value
            ?? principal.FindFirst("name")?.Value);
    }

    [Fact]
    public void ValidateToken_Tampered_ReturnsNull()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(1, "bob");
        var bad = token[..^4] + "xxxx";
        Assert.Null(svc.ValidateToken(bad));
    }

    [Fact]
    public void ValidateToken_WrongIssuer_ReturnsNull()
    {
        var other = new JwtService(new JwtSettings
        {
            Key = "test-secret-key-that-is-long-enough-32b!",
            Issuer = "other-issuer",
            Audience = "delegat-test-audience"
        });
        var token = other.GenerateToken(1, "x");
        Assert.Null(CreateService().ValidateToken(token));
    }

    [Fact]
    public void GenerateRefreshToken_RememberMe_HasLongerExpiry()
    {
        var svc = CreateService();
        var (_, shortExp) = svc.GenerateRefreshToken(rememberMe: false);
        var (_, longExp) = svc.GenerateRefreshToken(rememberMe: true);
        Assert.True(longExp > shortExp);
        Assert.True((longExp - DateTime.UtcNow).TotalDays > 20);
    }

    [Fact]
    public void GenerateRefreshToken_IsUnique()
    {
        var svc = CreateService();
        var (a, _) = svc.GenerateRefreshToken();
        var (b, _) = svc.GenerateRefreshToken();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ValidateExpiredToken_AcceptsValidShapeIgnoringLifetime()
    {
        var svc = CreateService();
        var token = svc.GenerateToken(7, "carol");
        var principal = svc.ValidateExpiredToken(token);
        Assert.NotNull(principal);
        Assert.Equal("7", principal!.FindFirst("userId")?.Value);
    }
}
