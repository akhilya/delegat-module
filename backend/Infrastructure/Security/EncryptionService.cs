using System.Security.Cryptography;
using System.Text;

namespace Delegat.Infrastructure.Security;

/// <summary>
/// Encrypts secrets with AES-256-GCM. Legacy AES-CBC ciphertexts (no prefix) remain decryptable
/// so existing credentials can be read and re-encrypted on next write.
/// </summary>
public class EncryptionService
{
    private const string GcmPrefix = "gcm:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public EncryptionService(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32)
            throw new ArgumentException("ENCRYPTION_KEY must be a 32-byte (256-bit) Base64-encoded key.");
    }

    public string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var result = new byte[NonceSize + cipherBytes.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSize, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + cipherBytes.Length, TagSize);

        return GcmPrefix + Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            throw new ArgumentException("Ciphertext is empty.", nameof(cipherText));

        if (cipherText.StartsWith(GcmPrefix, StringComparison.Ordinal))
        {
            var payload = cipherText[GcmPrefix.Length..];
            if (!TryFromBase64(payload, out _))
                throw new CryptographicException("Invalid GCM ciphertext encoding.");
            return DecryptGcm(payload);
        }

        // Legacy AES-CBC (IV || ciphertext), no authentication
        if (!TryFromBase64(cipherText, out _))
            throw new CryptographicException(
                "Ciphertext is not valid Base64 (value may be plaintext or corrupted).");

        return DecryptLegacyCbc(cipherText);
    }

    /// <summary>
    /// True when <paramref name="value"/> looks like an AES ciphertext produced by this service
    /// (gcm:Base64 or legacy Base64 CBC). Plain API keys and JSON are not ciphertext.
    /// </summary>
    public bool IsCiphertext(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        if (value.StartsWith(GcmPrefix, StringComparison.Ordinal))
            return TryFromBase64(value[GcmPrefix.Length..], out var gcmBytes)
                   && gcmBytes.Length >= NonceSize + TagSize;

        if (!TryFromBase64(value, out var legacyBytes))
            return false;

        // IV (16) + at least one cipher byte
        return legacyBytes.Length >= 17;
    }

    /// <summary>
    /// True when the value is ciphertext from this service (gcm: or legacy Base64 CBC).
    /// Plain JSON credentials and plain API keys return false.
    /// </summary>
    public bool IsEncrypted(string value) => IsCiphertext(value);

    private static bool TryFromBase64(string s, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(s);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private string DecryptGcm(string base64Payload)
    {
        var full = Convert.FromBase64String(base64Payload);
        if (full.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid GCM ciphertext.");

        var nonce = full.AsSpan(0, NonceSize);
        var tag = full.AsSpan(full.Length - TagSize, TagSize);
        var cipher = full.AsSpan(NonceSize, full.Length - NonceSize - TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private string DecryptLegacyCbc(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[16];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, 16);
        aes.IV = iv;

        var cipherBytes = new byte[fullCipher.Length - 16];
        Buffer.BlockCopy(fullCipher, 16, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
