using Delegat.Services.PlatformLink;

namespace Delegat.Tests.Marketplace;

public class OzonOAuthTransactionTests
{
    [Fact]
    public void TryParseCookie_MatchingNonce_ReturnsStoreId()
    {
        const string nonce = "0123456789abcdef0123456789abcdef";

        var valid = OzonOAuthTransaction.TryParseCookie($"42.{nonce}", nonce, out var storeId);

        Assert.True(valid);
        Assert.Equal(42, storeId);
    }

    [Fact]
    public void TryParseCookie_DifferentNonce_IsRejected()
    {
        var valid = OzonOAuthTransaction.TryParseCookie(
            "42.0123456789abcdef0123456789abcdef",
            "fedcba9876543210fedcba9876543210",
            out _);

        Assert.False(valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    public void TryParseCookie_MissingOrMalformedNonce_IsRejected(string? nonce)
    {
        Assert.False(OzonOAuthTransaction.TryParseCookie(
            "42.0123456789abcdef0123456789abcdef", nonce, out _));
    }
}
