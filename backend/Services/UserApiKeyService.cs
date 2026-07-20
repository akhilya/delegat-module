using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Services;

public class UserApiKeyService
{
    private readonly DataContext _db;

    public UserApiKeyService(DataContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<object>> ListAsync(int userId, CancellationToken ct = default)
    {
        var keys = await _db.userApiKeys
            .AsNoTracking()
            .Where(k => k.userId == userId)
            .OrderByDescending(k => k.createdAt)
            .ToListAsync(ct);

        return keys.Select(MapToListDto).ToList();
    }

    public async Task<(object dto, string plaintext)> CreateAsync(int userId, string name, CancellationToken ct = default)
    {
        name = name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            throw ApiException.BadRequest("Key name is required");
        if (name.Length > 100)
            throw ApiException.BadRequest("Key name is too long");

        var plaintext = GenerateKey();
        var entity = new UserApiKey
        {
            userId = userId,
            name = name,
            keyPrefix = "dk_",
            keyHash = ApiKeyAuthenticationHandler.HashKey(plaintext),
            last4 = plaintext[^4..],
            createdAt = DateTime.UtcNow
        };

        _db.userApiKeys.Add(entity);
        await _db.SaveChangesAsync(ct);

        return (MapToListDto(entity), plaintext);
    }

    public async Task RevokeAsync(int userId, int keyId, CancellationToken ct = default)
    {
        var key = await _db.userApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.userId == userId, ct);

        if (key == null)
            throw ApiException.NotFound("API key not found");

        if (key.revokedAt == null)
        {
            key.revokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private static object MapToListDto(UserApiKey k) => new
    {
        id = k.Id,
        name = k.name,
        keyPrefix = k.keyPrefix,
        last4 = k.last4,
        createdAt = k.createdAt,
        lastUsedAt = k.lastUsedAt,
        revokedAt = k.revokedAt,
        isRevoked = k.revokedAt != null,
        displayHint = $"{k.keyPrefix}…{k.last4}"
    };

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(30);
        var token = Convert.ToHexString(bytes).ToLowerInvariant();
        return "dk_" + token;
    }
}
