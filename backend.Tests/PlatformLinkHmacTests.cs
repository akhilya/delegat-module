using Delegat.Services.PlatformLink;

namespace Delegat.Tests;

public class PlatformLinkHmacTests
{
    [Fact]
    public void Sign_IsDeterministic_AndLowercaseHex()
    {
        var canonical = PlatformLinkHmac.BuildCanonical("GET", "/platform/v1/catalog/currencies", 1_700_000_000, PlatformLinkHmac.Sha256Hex(""));
        var a = PlatformLinkHmac.Sign("secret", canonical);
        var b = PlatformLinkHmac.Sign("secret", canonical);
        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Sign_DiffersForDifferentSecrets()
    {
        var canonical = PlatformLinkHmac.BuildCanonical("GET", "/x", 1, "abc");
        var a = PlatformLinkHmac.Sign("a", canonical);
        var b = PlatformLinkHmac.Sign("b", canonical);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TimestampsWithinSkew_AcceptsNearNow()
    {
        var now = DateTimeOffset.Parse("2026-07-21T12:00:00Z");
        Assert.True(PlatformLinkHmac.TimestampsWithinSkew(now.ToUnixTimeSeconds(), TimeSpan.FromMinutes(5), now));
        Assert.False(PlatformLinkHmac.TimestampsWithinSkew(now.ToUnixTimeSeconds() - 600, TimeSpan.FromMinutes(5), now));
    }

    [Fact]
    public void Sha256Hex_EmptyBody_IsKnownValue()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            PlatformLinkHmac.Sha256Hex(""));
    }
}
