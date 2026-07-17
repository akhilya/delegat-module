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
    public void Constructor_RejectsWrongKeyLength()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        Assert.Throws<ArgumentException>(() => new EncryptionService(shortKey));
    }
}
