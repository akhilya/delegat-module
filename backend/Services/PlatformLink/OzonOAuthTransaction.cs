using System.Globalization;
using System.Security.Cryptography;

namespace Delegat.Services.PlatformLink;

/// <summary>
/// Validates that an Ozon grant returns to the browser that started the
/// connection flow on this instance.
/// </summary>
internal static class OzonOAuthTransaction
{
    private const int NonceByteLength = 16;

    public static string CreateNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(NonceByteLength)).ToLowerInvariant();

    public static bool TryParseCookie(string? cookie, string? returnedNonce, out int storeId)
    {
        storeId = default;
        if (string.IsNullOrWhiteSpace(cookie) || !IsValidNonce(returnedNonce))
            return false;

        var separator = cookie.IndexOf('.');
        if (separator <= 0
            || separator == cookie.Length - 1
            || !int.TryParse(cookie.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out storeId)
            || storeId <= 0)
        {
            return false;
        }

        var cookieNonce = cookie[(separator + 1)..];
        if (!IsValidNonce(cookieNonce))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(cookieNonce),
            Convert.FromHexString(returnedNonce!));
    }

    public static bool IsValidNonce(string? nonce)
    {
        if (nonce is null || nonce.Length != NonceByteLength * 2)
            return false;

        foreach (var character in nonce)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }
}
