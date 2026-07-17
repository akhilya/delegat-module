using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Delegat.Models;

namespace Delegat.Infrastructure.Auth;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = PublicApiAuthorizeAttribute.Scheme;

    private readonly DataContext _db;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        DataContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
            return AuthenticateResult.NoResult();

        var header = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawKey = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(rawKey) || !rawKey.StartsWith("dk_", StringComparison.Ordinal))
            return AuthenticateResult.Fail("Invalid API key");

        var hash = HashKey(rawKey);
        var apiKey = await _db.userApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.keyHash == hash && k.revokedAt == null);

        if (apiKey == null)
            return AuthenticateResult.Fail("Invalid API key");

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == apiKey.userId && u.isActive);

        if (user == null)
            return AuthenticateResult.Fail("User is inactive");

        try
        {
            await _db.userApiKeys
                .Where(k => k.Id == apiKey.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.lastUsedAt, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to update lastUsedAt for API key {KeyId}", apiKey.Id);
        }

        var claims = new[]
        {
            new Claim("userId", user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? $"user-{user.Id}"),
            new Claim("apiKeyId", apiKey.Id.ToString()),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        return Response.WriteAsJsonAsync(new
        {
            error = new { code = "unauthorized", message = "Invalid or missing API key" }
        });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        return Response.WriteAsJsonAsync(new
        {
            error = new { code = "forbidden", message = "Access denied" }
        });
    }

    public static string HashKey(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
