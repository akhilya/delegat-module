using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

    private const string COOKIE_TOKEN_NAME = "access_token";

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
    public async Task<IActionResult> PostSignInUser([FromBody] SignInForm input)
    {
        var user = await _userManager.FindByNameAsync(input.userName);
        if (user == null)
            throw ApiException.Unauthorized("Неверные учётные данные");

        var result = await _signInManager.CheckPasswordSignInAsync(user, input.password, false);
        if (!result.Succeeded)
            throw ApiException.Unauthorized("Неверные учётные данные");

        var token = _jwtService.GenerateToken(user.Id, user.UserName ?? "", input.rememberMe);
        SetTokenCookie(token, input.rememberMe);

        return Ok(new
        {
            user = user.MapToDTO(_serviceProvider),
            next = "/products"
        });
    }

    [HttpPost]
    [Route("/auth/signUp")]
    [AllowAnonymous]
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

        var jwtToken = _jwtService.GenerateToken(user.Id, user.UserName ?? "", false);
        SetTokenCookie(jwtToken, false);

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
    public IActionResult PostLogOut()
    {
        if (Request.Cookies.Any(cookie => cookie.Key == COOKIE_TOKEN_NAME))
        {
            Response.Cookies.Delete(COOKIE_TOKEN_NAME);
            return Ok(new { next = "/signIn", success = true });
        }
        else
        {
            return Ok(new { success = false });
        }
    }

    private void SetTokenCookie(string token, bool rememberMe)
    {
        var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Lax,
            Expires = rememberMe ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(1)
        };
        Response.Cookies.Append(COOKIE_TOKEN_NAME, token, cookieOptions);
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
