using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;
using static Delegat.Models.User;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class JwtAuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IJwtService _jwtService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    private const string ACCESS_COOKIE = "access_token";
    private const string REFRESH_COOKIE = "refresh_token";

    private readonly IPermissionService _permissionService;
    private readonly LoginTicketProtector _loginTickets;
    private readonly IMemoryCache _cache;
    private readonly DataContext _db;

    private static readonly JsonSerializerOptions PasskeyJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public JwtAuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IJwtService jwtService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IPermissionService permissionService,
        LoginTicketProtector loginTickets,
        IMemoryCache cache,
        DataContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        _loginTickets = loginTickets;
        _cache = cache;
        _db = db;
    }

    /// <summary>
    /// Console SSO landing: the Control Plane "open cabinet" button bounces the
    /// browser here with a one-time code. The code is redeemed over Platform
    /// Link (HMAC instance auth, not the user's session); the returned account
    /// email must map to an existing local user. When none matches, a console
    /// org owner falls back to the cabinet administrator - equivalent to the
    /// console's reset-admin capability, so no extra privilege is granted.
    /// Normal auth rules (isActive, lockout) still apply; this endpoint only
    /// issues the same token pair a password sign-in would.
    /// </summary>
    [HttpGet]
    [Route("/auth/platformSso")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> GetPlatformSso(
        [FromQuery] string? code,
        CancellationToken ct)
    {
        var platformLink = _serviceProvider.GetService<Delegat.Services.PlatformLink.IPlatformLinkClient>();
        if (platformLink is null || !platformLink.IsEnabled)
            return Redirect("/signIn?ssoError=platform_link_disabled");
        if (string.IsNullOrWhiteSpace(code))
            return Redirect("/signIn?ssoError=code_required");

        Delegat.Services.PlatformLink.PlatformInstanceSsoDto? identity;
        try
        {
            identity = await platformLink.ExchangeInstanceSsoAsync(code.Trim(), ct);
        }
        catch
        {
            return Redirect("/signIn?ssoError=exchange_failed");
        }
        if (identity is null || string.IsNullOrWhiteSpace(identity.Email))
            return Redirect("/signIn?ssoError=invalid_code");

        var user = await _userManager.FindByEmailAsync(identity.Email.Trim());
        if (user is null && string.Equals(identity.Role, "owner", StringComparison.OrdinalIgnoreCase))
            user = await _userManager.Users.FirstOrDefaultAsync(u => u.isAdmin && u.isActive, ct);

        if (user is null)
            return Redirect("/signIn?ssoError=user_not_found");
        if (!user.isActive)
            return Redirect("/signIn?ssoError=user_deactivated");
        if (await _userManager.IsLockedOutAsync(user))
            return Redirect("/signIn?ssoError=account_locked");

        await IssueTokens(user.Id, user.UserName ?? "", rememberMe: false);
        if (user.selectedStoreId != null)
            SetSelectedStoreCookie(user.selectedStoreId.Value);

        return Redirect("/products");
    }

    [HttpPost]
    [Route("/auth/signIn")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PostSignInUser([FromBody] SignInForm input)
    {
        var userName = (input.userName ?? "").Trim();
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null && userName.Contains('@'))
            user = await _userManager.FindByEmailAsync(userName);
        if (user == null)
            throw ApiException.Unauthorized(code: "invalid_credentials");

        if (!user.isActive)
            throw ApiException.Forbidden(code: "user_deactivated");

        var result = await _signInManager.CheckPasswordSignInAsync(user, input.password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            throw ApiException.TooManyRequests(code: "account_locked");
        if (!result.Succeeded)
            throw ApiException.Unauthorized(code: "invalid_credentials");

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return Ok(new
            {
                status = "twoFactorRequired",
                loginTicket = _loginTickets.Protect(user.Id, input.rememberMe),
            });
        }

        await IssueTokens(user.Id, user.UserName ?? "", input.rememberMe);

        return Ok(new
        {
            user = await MapAuthUserDtoAsync(user),
            next = "/products"
        });
    }

    [HttpPost]
    [Route("/auth/signIn/totp")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PostSignInTotp([FromBody] TotpSignInForm input)
    {
        var user = await UserFromTicketAsync(input.loginTicket);
        var code = TotpUri.NormalizeCode(input.code);
        var rememberMe = input.rememberMe ?? TicketRememberMe(input.loginTicket);
        if (string.IsNullOrEmpty(code)
            || !await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                code))
        {
            await _userManager.AccessFailedAsync(user);
            throw ApiException.Unauthorized(code: "invalid_totp");
        }

        if (!_loginTickets.TryConsume(input.loginTicket))
            throw ApiException.Unauthorized(code: "invalid_login_ticket");

        await _userManager.ResetAccessFailedCountAsync(user);
        return await CompletePasswordlessSignInAsync(user, rememberMe);
    }

    [HttpPost]
    [Route("/auth/signIn/recovery")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PostSignInRecovery([FromBody] RecoverySignInForm input)
    {
        var user = await UserFromTicketAsync(input.loginTicket);
        var rememberMe = input.rememberMe ?? TicketRememberMe(input.loginTicket);
        var code = TotpUri.NormalizeCode(input.code);
        var redeemed = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
        if (!redeemed.Succeeded)
        {
            await _userManager.AccessFailedAsync(user);
            throw ApiException.Unauthorized(code: "invalid_recovery");
        }

        if (!_loginTickets.TryConsume(input.loginTicket))
            throw ApiException.Unauthorized(code: "invalid_login_ticket");

        await _userManager.ResetAccessFailedCountAsync(user);
        return await CompletePasswordlessSignInAsync(user, rememberMe);
    }

    [HttpPost]
    [Route("/auth/signIn/passkey/options")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public IActionResult PostSignInPasskeyOptions([FromBody] PasskeySignInOptionsForm? _)
    {
        // Empty allowCredentials: discoverable passkeys. A username here would fill the
        // list and also leak whether that account exists and has a passkey.
        var fido2 = PasskeyFido2.Create(Request);
        var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Preferred,
        });
        PasskeyFido2.SaveChallenge(HttpContext, _cache, "assertion", options.ToJson());
        return Content(options.ToJson(), "application/json");
    }

    [HttpPost]
    [Route("/auth/signIn/passkey")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PostSignInPasskey([FromBody] JsonElement body)
    {
        var json = PasskeyFido2.TakeChallenge(HttpContext, _cache, "assertion")
            ?? throw ApiException.Unauthorized(code: "challenge_expired");
        var options = AssertionOptions.FromJson(json);
        var assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(body.GetRawText(), PasskeyJson)
            ?? throw ApiException.Unauthorized(code: "passkey_failed");

        var cred = await _db.webAuthnCredentials.FirstOrDefaultAsync(c => c.credentialId == assertion.RawId)
            ?? throw ApiException.Unauthorized(code: "passkey_failed");

        var fido2 = PasskeyFido2.Create(Request);
        var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
        {
            AssertionResponse = assertion,
            OriginalOptions = options,
            StoredPublicKey = cred.publicKey,
            StoredSignatureCounter = cred.signCount,
            IsUserHandleOwnerOfCredentialIdCallback = (args, _) =>
                Task.FromResult(
                    cred.userHandle.AsSpan().SequenceEqual(args.UserHandle)
                    && cred.credentialId.AsSpan().SequenceEqual(args.CredentialId)),
        });

        cred.signCount = result.SignCount;
        cred.lastUsedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var user = await _userManager.FindByIdAsync(cred.userId.ToString())
            ?? throw ApiException.Unauthorized(code: "passkey_failed");
        if (!user.isActive)
            throw ApiException.Forbidden(code: "user_deactivated");
        if (await _userManager.IsLockedOutAsync(user))
            throw ApiException.TooManyRequests(code: "account_locked");

        var rememberMe = body.TryGetProperty("rememberMe", out var rm) && rm.ValueKind == JsonValueKind.True;
        return await CompletePasswordlessSignInAsync(user, rememberMe);
    }

    private async Task<IActionResult> CompletePasswordlessSignInAsync(User user, bool rememberMe)
    {
        await IssueTokens(user.Id, user.UserName ?? "", rememberMe);
        return Ok(new
        {
            user = await MapAuthUserDtoAsync(user),
            next = "/products"
        });
    }

    private async Task<User> UserFromTicketAsync(string? ticket)
    {
        if (!_loginTickets.TryUnprotect(ticket, out var payload))
            throw ApiException.Unauthorized(code: "invalid_login_ticket");

        var user = await _userManager.FindByIdAsync(payload.UserId.ToString())
            ?? throw ApiException.Unauthorized(code: "invalid_credentials");
        if (!user.isActive)
            throw ApiException.Forbidden(code: "user_deactivated");
        if (await _userManager.IsLockedOutAsync(user))
            throw ApiException.TooManyRequests(code: "account_locked");
        return user;
    }

    private bool TicketRememberMe(string? ticket) =>
        _loginTickets.TryUnprotect(ticket, out var payload) && payload.RememberMe;

    [HttpPost]
    [Route("/auth/signUp")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PostSignUpUser([FromBody] SignUpForm input, string token)
    {
        var tokenRepo = _unitOfWork.Repository<InvitationToken>();
        var tokenEntity = await tokenRepo.FindAsync(t => t.token == token);

        if (tokenEntity == null)
            throw ApiException.BadRequest(code: "invalid_token");

        if (tokenEntity.used)
            throw ApiException.BadRequest(code: "invalid_token");

        var user = new User { UserName = input.userName, isActive = true };
        var result = await _userManager.CreateAsync(user, input.password);
        result.ThrowIfFailed();

        // Mark token as used
        tokenEntity.UpdateFill(new InvitationToken.UseForm(user.Id));
        await tokenRepo.UpdateAsync(tokenEntity);

        // Add user to all stores
        var storeRepo = _unitOfWork.Repository<Store>();
        var stores = await storeRepo.GetAllAsync();
        foreach (var store in stores)
        {
            store.UpdateFill(new Store.UserJoinForm(user));
            await storeRepo.UpdateAsync(store);
        }

        await _unitOfWork.SaveChangesAsync();

        await _permissionService.EnsureRolesSeededAsync();
        await _userManager.AddToRoleAsync(user, AppRoles.Viewer);

        await IssueTokens(user.Id, user.UserName ?? "", false);

        return Ok(new
        {
            user = await MapAuthUserDtoAsync(user),
            next = "/signIn"
        });
    }

    [HttpGet]
    [Route("auth/isAuthenticated")]
    [AllowAnonymous]
    public async Task<IActionResult> GetIsAuthenticated()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Ok(new
            {
                isAuthenticated = false
            });
        }

        var user = await _userManager.FindByIdAsync(userId);
        return Ok(new
        {
            isAuthenticated = user?.isActive == true
        });
    }

    [HttpGet]
    [Route("auth/me")]
    public async Task<IActionResult> GetMe()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
            throw ApiException.Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.isActive)
            throw ApiException.NotFound(code: "user_not_found");

        // Sync selected_store_id cookie with DB value
        if (user.selectedStoreId != null)
            SetSelectedStoreCookie(user.selectedStoreId.Value);

        return Ok(await MapAuthUserDtoAsync(user));
    }

    private async Task<User.UserDTO> MapAuthUserDtoAsync(User user)
    {
        var dto = user.MapToDTO(_serviceProvider);
        var access = await _permissionService.GetUserAccessAsync(user.Id);
        dto.roles = access.Roles;
        dto.permissions = access.EffectivePermissions;

        var logging = _serviceProvider.GetService<Delegat.Services.ILoggingSettingsService>();
        if (logging != null)
        {
            dto.logErrorsEnabled = logging.IsErrorLoggingEnabled;
            dto.logApiRequestsEnabled = logging.IsApiRequestLoggingEnabled;
            dto.logAdapterHttpEnabled = logging.IsAdapterHttpLoggingEnabled;
        }
        else
        {
            dto.logErrorsEnabled = Delegat.Infrastructure.Configuration.FeatureFlags.IsErrorLoggingEnabled();
            dto.logApiRequestsEnabled = Delegat.Infrastructure.Configuration.FeatureFlags.IsApiRequestLoggingEnabled();
            dto.logAdapterHttpEnabled = Delegat.Infrastructure.Configuration.FeatureFlags.IsAdapterHttpLoggingEnabled();
        }

        var publicApi = _serviceProvider.GetService<Delegat.Services.IPublicApiSettingsService>();
        dto.apiUsageEnabled = Delegat.Infrastructure.Configuration.FeatureFlags.IsApiUsageEnabled();
        dto.publicApiEnabled = dto.apiUsageEnabled
            && (publicApi?.IsPublicApiEnabled
                ?? Delegat.Infrastructure.Configuration.FeatureFlags.IsPublicApiEnabled());

        return dto;
    }

    [HttpPost]
    [Route("auth/logOut")]
    public async Task<IActionResult> PostLogOut()
    {
        // Revoke all refresh tokens for the user
        var userIdStr = _userManager.GetUserId(User);
        if (int.TryParse(userIdStr, out var userId))
        {
            var refreshRepo = _unitOfWork.Repository<RefreshToken>();
            var tokens = await refreshRepo.FindAllAsync(rt => rt.userId == userId && rt.revokedAt == null);
            foreach (var rt in tokens)
            {
                rt.revokedAt = DateTime.UtcNow;
                refreshRepo.UpdateWithoutSave(rt);
            }
            await _unitOfWork.SaveChangesAsync();
        }

        Response.Cookies.Delete(ACCESS_COOKIE, new CookieOptions { Path = "/" });
        Response.Cookies.Delete(REFRESH_COOKIE, new CookieOptions { Path = "/" });
        Response.Cookies.Delete("selected_store_id");

        return Ok(new { next = "/signIn", success = true });
    }

    [HttpPost]
    [Route("/auth/refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> PostRefresh()
    {
        if (!Request.Cookies.TryGetValue(REFRESH_COOKIE, out var refreshTokenValue) || string.IsNullOrEmpty(refreshTokenValue))
            throw ApiException.Unauthorized(code: "refresh_token_missing");

        var refreshRepo = _unitOfWork.Repository<RefreshToken>();

        // Capture results from within the transaction
        string resultAccessToken = null!;
        string resultRefreshToken = null!;
        bool resultRememberMe = false;

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var refreshToken = await refreshRepo.FindAsync(rt => rt.token == refreshTokenValue);

            if (refreshToken == null || refreshToken.IsExpired)
                throw ApiException.Unauthorized(code: "invalid_token");

            var userId = refreshToken.userId;
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null || !user.isActive)
                throw ApiException.Unauthorized(code: "user_not_found");

            var rememberMe = refreshToken.expiresAt > refreshToken.createdAt.AddDays(14);
            resultRememberMe = rememberMe;

            // CASE 1: Token already revoked - find latest active token (multi-tab scenario)
            // Another tab already rotated this token; just hand the client the current active one.
            if (refreshToken.IsRevoked)
            {
                var activeToken = await refreshRepo.FindAsync(rt =>
                    rt.userId == userId &&
                    rt.revokedAt == null &&
                    rt.expiresAt > DateTime.UtcNow);

                if (activeToken == null)
                    throw ApiException.Unauthorized(code: "invalid_token");

                resultAccessToken = _jwtService.GenerateToken(userId, user.UserName ?? "");
                resultRefreshToken = activeToken.token;
                return;
            }

            // CASE 2: Token is active - normal rotation
            refreshToken.revokedAt = DateTime.UtcNow;
            refreshRepo.UpdateWithoutSave(refreshToken);

            var (newRefreshTokenValue, newRefreshExpiresAt) = _jwtService.GenerateRefreshToken(rememberMe);
            var newRefreshToken = new RefreshToken
            {
                userId = userId,
                token = newRefreshTokenValue,
                expiresAt = newRefreshExpiresAt,
                createdAt = DateTime.UtcNow
            };
            await refreshRepo.AddWithoutSaveAsync(newRefreshToken);

            // Cleanup stale revoked tokens (older than 1 day)
            var staleTokens = await refreshRepo.FindAllAsync(rt =>
                rt.userId == userId &&
                rt.revokedAt != null &&
                rt.revokedAt < DateTime.UtcNow.AddDays(-1));
            if (staleTokens.Any())
                refreshRepo.DeleteRange(staleTokens);

            await _unitOfWork.SaveChangesAsync();

            resultAccessToken = _jwtService.GenerateToken(userId, user.UserName ?? "");
            resultRefreshToken = newRefreshTokenValue;
        });

        SetAccessTokenCookie(resultAccessToken, resultRememberMe);
        SetRefreshTokenCookie(resultRefreshToken, resultRememberMe);
        return Ok();
    }

    private async Task IssueTokens(int userId, string userName, bool rememberMe)
    {
        var accessToken = _jwtService.GenerateToken(userId, userName);
        var (refreshTokenValue, refreshExpiresAt) = _jwtService.GenerateRefreshToken(rememberMe);

        // Save refresh token to DB
        var refreshRepo = _unitOfWork.Repository<RefreshToken>();

        // Cleanup only expired or revoked tokens (preserve active sessions on other devices)
        var deadTokens = await refreshRepo.FindAllAsync(rt =>
            rt.userId == userId &&
            (rt.revokedAt != null || rt.expiresAt <= DateTime.UtcNow));
        if (deadTokens.Any())
            refreshRepo.DeleteRange(deadTokens);

        var refreshToken = new RefreshToken
        {
            userId = userId,
            token = refreshTokenValue,
            expiresAt = refreshExpiresAt,
            createdAt = DateTime.UtcNow
        };
        await refreshRepo.AddAsync(refreshToken);
        await _unitOfWork.SaveChangesAsync();

        SetAccessTokenCookie(accessToken, rememberMe);
        SetRefreshTokenCookie(refreshTokenValue, rememberMe);
    }

    private void SetAccessTokenCookie(string token, bool rememberMe)
    {
        Response.Cookies.Append(ACCESS_COOKIE, token, AuthCookie.AccessOrRefresh(Request, rememberMe));
    }

    private void SetRefreshTokenCookie(string token, bool rememberMe)
    {
        Response.Cookies.Append(REFRESH_COOKIE, token, AuthCookie.AccessOrRefresh(Request, rememberMe));
    }

    private void SetSelectedStoreCookie(int storeId)
    {
        Response.Cookies.Append(
            "selected_store_id",
            storeId.ToString(),
            AuthCookie.SelectedStore(Request));
    }

    public struct SignInForm
    {
        public string userName { get; set; }
        public string password { get; set; }
        public bool rememberMe { get; set; }
    }

    public struct TotpSignInForm
    {
        public string loginTicket { get; set; }
        public string code { get; set; }
        public bool? rememberMe { get; set; }
    }

    public struct RecoverySignInForm
    {
        public string loginTicket { get; set; }
        public string code { get; set; }
        public bool? rememberMe { get; set; }
    }

    public struct PasskeySignInOptionsForm
    {
        public string? userName { get; set; }
    }

    public struct SignUpForm
    {
        public string userName { get; set; }
        public string password { get; set; }
    }
}
