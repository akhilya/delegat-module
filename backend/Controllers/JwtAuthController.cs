using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
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

    public JwtAuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IJwtService jwtService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IPermissionService permissionService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
    }

    [HttpPost]
    [Route("/auth/signIn")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PostSignInUser([FromBody] SignInForm input)
    {
        var user = await _userManager.FindByNameAsync(input.userName);
        if (user == null)
            throw ApiException.Unauthorized("Invalid credentials", code: "invalid_credentials");

        if (!user.isActive)
            throw ApiException.Forbidden("User is deactivated", code: "user_deactivated");

        var result = await _signInManager.CheckPasswordSignInAsync(user, input.password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return StatusCode(429, new { title = "Account locked. Try again later.", status = 429 });
        if (!result.Succeeded)
            throw ApiException.Unauthorized("Invalid credentials", code: "invalid_credentials");

        await IssueTokens(user.Id, user.UserName ?? "", input.rememberMe);

        return Ok(new
        {
            user = await MapAuthUserDtoAsync(user),
            next = "/products"
        });
    }

    [HttpPost]
    [Route("/auth/signUp")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PostSignUpUser([FromBody] SignUpForm input, string token)
    {
        var tokenRepo = _unitOfWork.Repository<InvitationToken>();
        var tokenEntity = await tokenRepo.FindAsync(t => t.token == token);

        if (tokenEntity == null)
            throw ApiException.BadRequest("Invalid token", code: "invalid_token");

        if (tokenEntity.used)
            throw ApiException.BadRequest("Token has already been used", code: "invalid_token");

        var user = new User { UserName = input.userName, isActive = true };
        var result = await _userManager.CreateAsync(user, input.password);

        if (!result.Succeeded)
            throw ApiException.BadRequest(result.Errors.Select(e => e.Description).ToArray());

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
            throw ApiException.NotFound("User not found", code: "user_not_found");

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
            throw ApiException.Unauthorized("Refresh token is missing");

        var refreshRepo = _unitOfWork.Repository<RefreshToken>();

        // Capture results from within the transaction
        string resultAccessToken = null!;
        string resultRefreshToken = null!;
        bool resultRememberMe = false;

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var refreshToken = await refreshRepo.FindAsync(rt => rt.token == refreshTokenValue);

            if (refreshToken == null || refreshToken.IsExpired)
                throw ApiException.Unauthorized("Refresh token is invalid", code: "invalid_token");

            var userId = refreshToken.userId;
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null || !user.isActive)
                throw ApiException.Unauthorized("User not found", code: "user_not_found");

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
                    throw ApiException.Unauthorized("Refresh token is invalid", code: "invalid_token");

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
        var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
        Response.Cookies.Append(ACCESS_COOKIE, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(7)
        });
    }

    private void SetRefreshTokenCookie(string token, bool rememberMe)
    {
        var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
        Response.Cookies.Append(REFRESH_COOKIE, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(7)
        });
    }

    private void SetSelectedStoreCookie(int storeId)
    {
        var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
        Response.Cookies.Append("selected_store_id", storeId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddDays(30)
        });
    }

    public struct SignInForm
    {
        public string userName { get; set; }
        public string password { get; set; }
        public bool rememberMe { get; set; }
    }

    public struct SignUpForm
    {
        public string userName { get; set; }
        public string password { get; set; }
    }
}
