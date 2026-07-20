using Delegat.Infrastructure.Security;

namespace Delegat.Tests;

public class EncryptionServiceTests
{
    private static EncryptionService Create()
    {
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        return new EncryptionService(key);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        var svc = Create();
        var plain = "{\"apiKey\":\"secret-value\"}";
        var cipher = svc.Encrypt(plain);
        Assert.NotEqual(plain, cipher);
        Assert.StartsWith("gcm:", cipher);
        Assert.Equal(plain, svc.Decrypt(cipher));
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertexts()
    {
        var svc = Create();
        var a = svc.Encrypt("same");
        var b = svc.Encrypt("same");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void IsEncrypted_JsonLooksUnencrypted()
    {
        var svc = Create();
        Assert.False(svc.IsEncrypted("{\"a\":1}"));
        Assert.True(svc.IsEncrypted(svc.Encrypt("x")));
    }

    /// <summary>
    /// Regression: plain API keys must not be treated as ciphertext. The previous
    /// heuristic (anything not starting with '{') caused Decrypt to throw on
    /// legacy plaintext rows and blocked AI model use until keys were re-entered.
    /// </summary>
    [Theory]
    [InlineData("sk-proj-plain-api-key-value")]
    [InlineData("not-base64!!!")]
    [InlineData("short")]
    [InlineData("")]
    public void IsCiphertext_PlainApiKeys_AreNotCiphertext(string value)
    {
        var svc = Create();
        Assert.False(svc.IsCiphertext(value));
        Assert.False(svc.IsEncrypted(value));
    }

    [Fact]
    public void IsCiphertext_GcmPrefixWithInvalidBase64_IsFalse()
    {
        var svc = Create();
        Assert.False(svc.IsCiphertext("gcm:not!!valid!!base64"));
    }

    [Fact]
    public void IsCiphertext_GcmPrefixWithTooShortPayload_IsFalse()
    {
        var svc = Create();
        // Valid Base64 but shorter than nonce(12)+tag(16)
        Assert.False(svc.IsCiphertext("gcm:" + Convert.ToBase64String(new byte[10])));
    }

    [Fact]
    public void IsCiphertext_ShortBase64_IsFalse()
    {
        var svc = Create();
        // Decodes to fewer than IV(16)+1 bytes — not a legacy CBC blob
        Assert.False(svc.IsCiphertext(Convert.ToBase64String(new byte[8])));
    }

    [Fact]
    public void IsCiphertext_LegacyCbcBlob_IsTrue()
    {
        var svc = Create();
        var legacy = CreateLegacyCbc("legacy-secret");
        Assert.True(svc.IsCiphertext(legacy));
        Assert.True(svc.IsEncrypted(legacy));
    }

    [Fact]
    public void Decrypt_RejectsPlaintextThatIsNotBase64()
    {
        var svc = Create();
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => svc.Decrypt("sk-plain-key-not-base64"));
    }

    [Fact]
    public void Decrypt_RejectsInvalidGcmEncoding()
    {
        var svc = Create();
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => svc.Decrypt("gcm:%%%not-base64%%%"));
    }

    [Fact]
    public void Decrypt_SupportsLegacyCbcCiphertext()
    {
        var svc = Create();
        var plain = "legacy-secret";
        var legacy = CreateLegacyCbc(plain);

        Assert.Equal(plain, svc.Decrypt(legacy));
    }

    [Fact]
    public void Constructor_RejectsWrongKeyLength()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        Assert.Throws<ArgumentException>(() => new EncryptionService(shortKey));
    }

    private static string CreateLegacyCbc(string plain)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        var key = Convert.FromBase64String(Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        aes.Key = key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plain);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(result);
    }
}
