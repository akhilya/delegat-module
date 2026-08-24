using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Delegat.Infrastructure.Auth;

/// <summary>Short-lived signed ticket between password check and TOTP/recovery (JWT is not issued yet).</summary>
public sealed class LoginTicketProtector
{
    public const string Purpose = "Delegat.LoginTicket.v1";
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _protector;
    private readonly IMemoryCache _cache;

    public LoginTicketProtector(IDataProtectionProvider provider, IMemoryCache cache)
    {
        _protector = provider.CreateProtector(Purpose);
        _cache = cache;
    }

    public string Protect(int userId, bool rememberMe, TimeSpan? lifetime = null)
    {
        var ttl = lifetime ?? DefaultLifetime;
        var ticketId = Guid.NewGuid();
        var payload = new LoginTicketPayload(
            userId,
            rememberMe,
            DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds(),
            ticketId);
        var token = _protector.Protect(JsonSerializer.Serialize(payload));
        _cache.Set(CacheKey(ticketId), true, ttl);
        return token;
    }

    public bool TryUnprotect(string? token, out LoginTicketPayload payload)
    {
        if (!TryRead(token, out payload))
            return false;
        return _cache.TryGetValue(CacheKey(payload.TicketId), out _);
    }

    public bool TryConsume(string? token)
    {
        if (!TryRead(token, out var payload))
            return false;
        if (!_cache.TryGetValue(CacheKey(payload.TicketId), out _))
            return false;
        _cache.Remove(CacheKey(payload.TicketId));
        return true;
    }

    private bool TryRead(string? token, out LoginTicketPayload payload)
    {
        payload = default;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<LoginTicketPayload>(_protector.Unprotect(token));
            if (parsed.UserId <= 0)
                return false;
            if (parsed.ExpiresUnix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return false;
            payload = parsed;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string CacheKey(Guid ticketId) => $"login-ticket:{ticketId:N}";
}

public readonly record struct LoginTicketPayload(int UserId, bool RememberMe, long ExpiresUnix, Guid TicketId);
