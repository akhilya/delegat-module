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
                rt.revokedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
        }

        Response.Cookies.Delete(ACCESS_COOKIE, new CookieOptions { Path = "/" });
        Response.Cookies.Delete(REFRESH_COOKIE, new CookieOptions { Path = "/" });

        return Ok(new { next = "/signIn", success = true });
    }

    [HttpPost]
    [Route("/auth/refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> PostRefresh()
    {
        // Read refresh token from cookie
        if (!Request.Cookies.TryGetValue(REFRESH_COOKIE, out var refreshTokenValue) || string.IsNullOrEmpty(refreshTokenValue))
            throw ApiException.Unauthorized("Refresh token отсутствует");

        // Find refresh token in DB
        var refreshRepo = _unitOfWork.Repository<RefreshToken>();
        var refreshToken = await refreshRepo.FindAsync(rt => rt.token == refreshTokenValue);
        if (refreshToken == null || !refreshToken.IsActive)
            throw ApiException.Unauthorized("Refresh token недействителен");

        var userId = refreshToken.userId;

        // Verify user still exists
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            throw ApiException.Unauthorized("Пользователь не найден");

        // Rotation: revoke old, create new
        refreshToken.revokedAt = DateTime.UtcNow;
        var rememberMe = refreshToken.expiresAt > refreshToken.createdAt.AddDays(14);
        var (newRefreshTokenValue, newRefreshExpiresAt) = _jwtService.GenerateRefreshToken(rememberMe);

        var newRefreshToken = new RefreshToken
        {
            userId = userId,
            token = newRefreshTokenValue,
            expiresAt = newRefreshExpiresAt,
            createdAt = DateTime.UtcNow
        };
        await refreshRepo.AddAsync(newRefreshToken);
        await _unitOfWork.SaveChangesAsync();

        // Issue new access token
        var newAccessToken = _jwtService.GenerateToken(userId, user.UserName ?? "");
        SetAccessTokenCookie(newAccessToken, rememberMe);
        SetRefreshTokenCookie(newRefreshTokenValue, rememberMe);

        return Ok();
    }

    private async Task IssueTokens(int userId, string userName, bool rememberMe)
    {
        var accessToken = _jwtService.GenerateToken(userId, userName);
        var (refreshTokenValue, refreshExpiresAt) = _jwtService.GenerateRefreshToken(rememberMe);

        // Save refresh token to DB
        var refreshRepo = _unitOfWork.Repository<RefreshToken>();

        // Cleanup old refresh tokens for this user
        var oldTokens = await refreshRepo.FindAllAsync(rt => rt.userId == userId);
        refreshRepo.DeleteRange(oldTokens);

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
