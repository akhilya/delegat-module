using System.Security.Cryptography;
using System.Text;

namespace Delegat.Services.Ai;

/// <summary>Signs short-lived plan approvals so client-saved chat JSON cannot mint executable plans.</summary>
public static class AiPlanProposalToken
{
    private const string Purpose = "Delegat.AiPlanProposal.v1";

    public static (string Token, string Nonce) Mint(
        string sessionId,
        int storeId,
        int userId,
        string stepsHash,
        DateTime expiresAtUtc)
    {
        var key = ReadSigningKey();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = Payload(nonce, sessionId, storeId, userId, stepsHash, expiresAtUtc);
        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return ($"{nonce}.{Base64Url(signature)}", nonce);
    }

    public static bool TryValidate(
        string? token,
        string sessionId,
        int storeId,
        int userId,
        string stepsHash,
        DateTime expiresAtUtc,
        out string nonce)
    {
        nonce = "";
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.', 2);
        if (parts.Length != 2 || parts[0].Length != 32 || !IsHex(parts[0])) return false;

        byte[] actual;
        try { actual = FromBase64Url(parts[1]); }
        catch (FormatException) { return false; }
        if (actual.Length != 32) return false;

        var key = ReadSigningKey();
        var payload = Payload(parts[0], sessionId, storeId, userId, stepsHash, expiresAtUtc);
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) return false;
        nonce = parts[0];
        return true;
    }

    private static string Payload(
        string nonce, string sessionId, int storeId, int userId, string stepsHash, DateTime expiresAtUtc) =>
        string.Join('\n', Purpose, nonce, sessionId, storeId, userId, stepsHash, expiresAtUtc.ToUniversalTime().Ticks);

    private static byte[] ReadSigningKey()
    {
        var encoded = Environment.GetEnvironmentVariable("ENCRYPTION_KEY");
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("ENCRYPTION_KEY is required to sign AI plan proposals.");
        var key = Convert.FromBase64String(encoded);
        if (key.Length != 32)
            throw new InvalidOperationException("ENCRYPTION_KEY must be a 32-byte Base64-encoded key.");
        return key;
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private static bool IsHex(string value) => value.All(Uri.IsHexDigit);
}
