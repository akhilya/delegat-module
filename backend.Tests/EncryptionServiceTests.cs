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

    [Fact]
    public void Decrypt_SupportsLegacyCbcCiphertext()
    {
        var svc = Create();
        // Produce a legacy CBC blob matching the previous format (IV || ciphertext), no gcm: prefix
        var plain = "legacy-secret";
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
        var legacy = Convert.ToBase64String(result);

        Assert.Equal(plain, svc.Decrypt(legacy));
    }

    [Fact]
    public void Constructor_RejectsWrongKeyLength()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        Assert.Throws<ArgumentException>(() => new EncryptionService(shortKey));
    }
}
