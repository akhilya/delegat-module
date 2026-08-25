using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class AccountSecurityController : ControllerBase
{
    private readonly UserManager<User> _users;
    private readonly ICurrentUserService _currentUser;
    private readonly DataContext _db;
    private readonly IMemoryCache _cache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AccountSecurityController(
        UserManager<User> users,
        ICurrentUserService currentUser,
        DataContext db,
        IMemoryCache cache)
    {
        _users = users;
        _currentUser = currentUser;
        _db = db;
        _cache = cache;
    }

    [HttpGet]
    [Route("/auth/account/security")]
    public async Task<IActionResult> GetSecurity()
    {
        var user = await RequireUserAsync();
        var totpEnabled = await _users.GetTwoFactorEnabledAsync(user);
        var passkeys = await _db.webAuthnCredentials.AsNoTracking()
            .Where(c => c.userId == user.Id)
            .OrderBy(c => c.createdAt)
            .Select(c => new
            {
                id = c.Id,
                nickname = c.nickname,
                createdAt = c.createdAt,
                lastUsedAt = c.lastUsedAt,
            })
            .ToListAsync();

        return Ok(new { totpEnabled, passkeys });
    }

    [HttpPost]
    [Route("/auth/account/totp/start")]
    public async Task<IActionResult> PostStartTotp([FromBody] PasswordConfirmForm input)
    {
        CabinetViewOnlyGuard.ThrowIfRestricted(User);
        var user = await RequirePasswordAsync(input.password);
        if (await _users.GetTwoFactorEnabledAsync(user))
            throw ApiException.Conflict("Authenticator is already enabled", code: "totp_already_enabled");

        await _users.ResetAuthenticatorKeyAsync(user);
        var key = await _users.GetAuthenticatorKeyAsync(user)
            ?? throw ApiException.InternalServerError("Could not create authenticator key", code: "authenticator_key_failed");

        return Ok(new
        {
            otpauthUri = TotpUri.Build(user.UserName ?? user.Id.ToString(), key),
            secret = TotpUri.FormatSecret(key),
            totpEnabled = false,
        });
    }

    [HttpPost]
    [Route("/auth/account/totp/confirm")]
    public async Task<IActionResult> PostConfirmTotp([FromBody] TotpCodeForm input)
    {
        CabinetViewOnlyGuard.ThrowIfRestricted(User);
        var user = await RequireUserAsync();
        if (await _users.GetTwoFactorEnabledAsync(user))
            throw ApiException.Conflict("Authenticator is already enabled", code: "totp_already_enabled");

        var code = TotpUri.NormalizeCode(input.code);
        var valid = await _users.VerifyTwoFactorTokenAsync(
            user,
            _users.Options.Tokens.AuthenticatorTokenProvider,
            code);
        if (!valid)
        {
            await _users.AccessFailedAsync(user);
            if (await _users.IsLockedOutAsync(user))
                throw ApiException.TooManyRequests("Account locked. Try again later.", code: "account_locked");
            throw ApiException.Unauthorized("Invalid authenticator code", code: "invalid_totp");
        }

        await _users.ResetAccessFailedCountAsync(user);
        await _users.SetTwoFactorEnabledAsync(user, true);
        var recovery = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return Ok(new { recoveryCodes = recovery?.ToArray() ?? [] });
    }

    [HttpPost]
    [Route("/auth/account/totp/disable")]
    public async Task<IActionResult> PostDisableTotp([FromBody] PasswordConfirmForm input)
    {
        CabinetViewOnlyGuard.ThrowIfRestricted(User);
        var user = await RequirePasswordAsync(input.password);
        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.ResetAuthenticatorKeyAsync(user);
        return Ok(new { ok = true });
    }

    [HttpPost]
    [Route("/auth/account/passkeys/options")]
    public async Task<IActionResult> PostPasskeyOptions([FromBody] PasswordConfirmForm input)
    {
        CabinetViewOnlyGuard.ThrowIfRestricted(User);
        var user = await RequirePasswordAsync(input.password);
        var existing = await _db.webAuthnCredentials.AsNoTracking()
            .Where(c => c.userId == user.Id)
            .Select(c => c.credentialId)
            .ToListAsync();

        var fido2 = PasskeyFido2.Create(Request);
        var options = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = PasskeyFido2.UserHandle(user.Id),
                Name = user.UserName ?? user.Id.ToString(),
                DisplayName = user.UserName ?? user.Id.ToString(),
            },
            ExcludeCredentials = existing.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = PasskeyFido2.CreateSelection(),
            AttestationPreference = AttestationConveyancePreference.None,
        });

        PasskeyFido2.SaveChallenge(HttpContext, _cache, $"attestation:{user.Id}", options.ToJson());
        return Content(options.ToJson(), "application/json");
    }

    [HttpPost]
    [Route("/auth/account/passkeys")]
    public async Task<IActionResult> PostRegisterPasskey([FromBody] JsonElement body)
    {
        CabinetViewOnlyGuard.ThrowIfRestricted(User);
        var user = await RequireUserAsync();
        var password = body.TryGetProperty("password", out var passwordEl) ? passwordEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(password) || !await _users.CheckPasswordAsync(user, password))
            throw ApiException.Unauthorized("Current password is incorrect", code: "password_mismatch");

        var json = PasskeyFido2.TakeChallenge(HttpContext, _cache, $"attestation:{user.Id}")
            ?? throw ApiException.BadRequest("Passkey challenge expired", code: "challenge_expired");

        var options = CredentialCreateOptions.FromJson(json);
        var attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(
            body.GetRawText(),
            JsonOptions)
            ?? throw ApiException.BadRequest("Attestation is required", code: "attestation_required");

        var fido2 = PasskeyFido2.Create(Request);
        var success = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = attestation,
            OriginalOptions = options,
            IsCredentialIdUniqueToUserCallback = async (args, _) =>
                !await _db.webAuthnCredentials.AnyAsync(c => c.credentialId == args.CredentialId),
        });

        _db.webAuthnCredentials.Add(new WebAuthnCredential
        {
            userId = user.Id,
            credentialId = success.Id,
            publicKey = success.PublicKey,
            userHandle = PasskeyFido2.UserHandle(user.Id),
            signCount = success.SignCount,
            nickname = PasskeyFido2.NormalizeNickname(
                body.TryGetProperty("nickname", out var nick) ? nick.GetString() : null),
            createdAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete]
    [Route("/auth/account/passkeys/{id:int}")]
    public async Task<IActionResult> DeletePasskey(int id, [FromBody] PasswordConfirmForm input)
    {
        CabinetViewOnlyGuard.ThrowIfRestricted(User);
        var user = await RequirePasswordAsync(input.password);
        var row = await _db.webAuthnCredentials.FirstOrDefaultAsync(c => c.Id == id && c.userId == user.Id)
            ?? throw ApiException.NotFound("Passkey not found", code: "passkey_not_found");
        _db.webAuthnCredentials.Remove(row);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private async Task<User> RequireUserAsync()
    {
        var userId = _currentUser.GetUserId();
        if (userId is null)
            throw ApiException.Unauthorized();

        var user = await _users.FindByIdAsync(userId.Value.ToString());
        if (user is null || !user.isActive)
            throw ApiException.Unauthorized();
        return user;
    }

    private async Task<User> RequirePasswordAsync(string? password)
    {
        var user = await RequireUserAsync();
        if (string.IsNullOrWhiteSpace(password) || !await _users.CheckPasswordAsync(user, password))
            throw ApiException.Unauthorized("Current password is incorrect", code: "password_mismatch");
        return user;
    }

    public struct PasswordConfirmForm
    {
        public string password { get; set; }
    }

    public struct TotpCodeForm
    {
        public string code { get; set; }
    }
}
