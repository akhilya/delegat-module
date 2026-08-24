using System.Security.Cryptography;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Delegat.Infrastructure.Auth;

public static class PasskeyFido2
{
    public const string ServerName = "Delegat";
    public const string ChallengeCookie = "delegat.webauthn";
    public static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);

    public static IFido2 Create(HttpRequest request) =>
        new Fido2(new Fido2Configuration
        {
            ServerDomain = PasskeyRelyingParty.Domain(request),
            ServerName = ServerName,
            Origins = new HashSet<string> { PasskeyRelyingParty.Origin(request) },
        });

    public static AuthenticatorSelection CreateSelection() => new()
    {
        ResidentKey = ResidentKeyRequirement.Required,
        RequireResidentKey = true,
        UserVerification = UserVerificationRequirement.Preferred,
    };

    public static byte[] UserHandle(int userId) => BitConverter.GetBytes(userId);

    public static void SaveChallenge(HttpContext http, IMemoryCache cache, string purpose, string json)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        cache.Set(CacheKey(purpose, token), json, ChallengeTtl);
        http.Response.Cookies.Append(ChallengeCookie, token, Cookie(http.Request));
    }

    public static string? TakeChallenge(HttpContext http, IMemoryCache cache, string purpose)
    {
        if (!http.Request.Cookies.TryGetValue(ChallengeCookie, out var token) || string.IsNullOrEmpty(token))
            return null;
        var key = CacheKey(purpose, token);
        if (!cache.TryGetValue(key, out string? json) || string.IsNullOrEmpty(json))
            return null;
        cache.Remove(key);
        http.Response.Cookies.Delete(ChallengeCookie, Cookie(http.Request));
        return json;
    }

    public static string? NormalizeNickname(string? nickname)
    {
        var name = nickname?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;
        return name.Length <= 128 ? name : name[..128];
    }

    private static string CacheKey(string purpose, string token) => $"passkey:{purpose}:{token}";

    private static CookieOptions Cookie(HttpRequest request) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = request.IsHttps,
        MaxAge = ChallengeTtl,
        Path = "/api/auth",
    };
}
