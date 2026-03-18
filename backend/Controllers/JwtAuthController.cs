using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using static PrintO.Models.User;

namespace PrintO.Controllers;

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

    public JwtAuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IJwtService jwtService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    [Route("/auth/signIn")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PostSignInUser([FromBody] SignInForm input)
    {
        var user = await _userManager.FindByNameAsync(input.userName);
        if (user == null)
            throw ApiException.Unauthorized("Неверные учётные данные");

        var result = await _signInManager.CheckPasswordSignInAsync(user, input.password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return StatusCode(429, new { title = "Account locked. Try again later.", status = 429 });
        if (!result.Succeeded)
            throw ApiException.Unauthorized("Неверные учётные данные");

        await IssueTokens(user.Id, user.UserName ?? "", input.rememberMe);

        return Ok(new
        {
            user = user.MapToDTO(_serviceProvider),
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
            throw ApiException.BadRequest("Недействительный токен");

        if (tokenEntity.used)
            throw ApiException.BadRequest("Токен уже использован");

        var user = new User { UserName = input.userName };
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

        await IssueTokens(user.Id, user.UserName ?? "", false);

        return Ok(new
        {
            user = user.MapToDTO(_serviceProvider),
            next = "/signIn"
        });
    }

    [HttpGet]
    [Route("auth/isAuthenticated")]
    [AllowAnonymous]
    public IActionResult GetIsAuthenticated()
    {
        var userId = _userManager.GetUserId(User);
        return Ok(new
        {
            isAuthenticated = !string.IsNullOrEmpty(userId)
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
        if (user == null)
            throw ApiException.NotFound("Пользователь не найден");

        // Sync selected_store_id cookie with DB value
        if (user.selectedStoreId != null)
            SetSelectedStoreCookie(user.selectedStoreId.Value);

        return Ok(user.MapToDTO(_serviceProvider));
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
            throw ApiException.Unauthorized("Refresh token отсутствует");

        var refreshRepo = _unitOfWork.Repository<RefreshToken>();

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            var refreshToken = await refreshRepo.FindAsync(rt => rt.token == refreshTokenValue);

            if (refreshToken == null || refreshToken.IsExpired)
                throw ApiException.Unauthorized("Refresh token недействителен");

            var userId = refreshToken.userId;
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null)
                throw ApiException.Unauthorized("Пользователь не найден");

            var rememberMe = refreshToken.expiresAt > refreshToken.createdAt.AddDays(14);

            // CASE 1: Token already revoked — find latest active token (multi-tab scenario)
            // Another tab already rotated this token; just hand the client the current active one.
            if (refreshToken.IsRevoked)
            {
                var activeToken = await refreshRepo.FindAsync(rt =>
                    rt.userId == userId &&
                    rt.revokedAt == null &&
                    rt.expiresAt > DateTime.UtcNow);

                if (activeToken == null)
                    throw ApiException.Unauthorized("Refresh token недействителен");

                await _unitOfWork.CommitTransactionAsync();

                // Reissue access token + set cookies to the active token
                var accessToken = _jwtService.GenerateToken(userId, user.UserName ?? "");
                SetAccessTokenCookie(accessToken, rememberMe);
                SetRefreshTokenCookie(activeToken.token, rememberMe);
                return Ok();
            }

            // CASE 2: Token is active — normal rotation
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
            await _unitOfWork.CommitTransactionAsync();

            var newAccessToken = _jwtService.GenerateToken(userId, user.UserName ?? "");
            SetAccessTokenCookie(newAccessToken, rememberMe);
            SetRefreshTokenCookie(newRefreshTokenValue, rememberMe);

            return Ok();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
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
            SameSite = SameSiteMode.Lax,
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
            SameSite = SameSiteMode.Lax,
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
            SameSite = SameSiteMode.Lax,
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
