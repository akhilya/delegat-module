using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using static Delegat.Models.Store;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class UserController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly UserManager<User> _userManager;

    public UserController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        UserManager<User> userManager)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _userManager = userManager;
    }

    [HttpGet]
    [Route("/users/")]
    public async Task<IActionResult> GetUsers()
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут просматривать пользователей");

        var users = await _userManager.Users
            .OrderByDescending(u => u.isActive)
            .ThenBy(u => u.UserName)
            .ToListAsync();

        return Ok(users.Select(u => u.MapToDTO(_serviceProvider)).ToList());
    }

    [HttpPatch]
    [Route("/users/{id}")]
    public async Task<IActionResult> PatchUser(int id, [FromBody] UpdateUserForm input)
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут редактировать пользователей");

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
            throw ApiException.NotFound("Пользователь не найден");

        if (input.userName != null)
        {
            var normalizedName = input.userName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw ApiException.BadRequest("Имя пользователя не может быть пустым");

            if (normalizedName.Length > Delegat.Models.User.USER_NAME_MAX_LENGTH)
                throw ApiException.BadRequest($"Имя пользователя не может быть длиннее {Delegat.Models.User.USER_NAME_MAX_LENGTH} символов");

            var duplicate = await _userManager.FindByNameAsync(normalizedName);
            if (duplicate != null && duplicate.Id != user.Id)
                throw ApiException.BadRequest("Пользователь с таким именем уже существует");

            user.UserName = normalizedName;
            user.NormalizedUserName = _userManager.NormalizeName(normalizedName);
        }

        if (input.isAdmin.HasValue)
        {
            if (user.Id == currentUser.Id && !input.isAdmin.Value)
                throw ApiException.BadRequest("Нельзя снять права администратора с самого себя");

            if (!input.isAdmin.Value)
            {
                var activeAdminCount = await _userManager.Users.CountAsync(u => u.isActive && u.isAdmin);
                if (user.isAdmin && activeAdminCount <= 1)
                    throw ApiException.BadRequest("Нельзя снять права с последнего активного администратора");
            }

            user.isAdmin = input.isAdmin.Value;
        }

        if (input.isActive.HasValue)
        {
            if (user.Id == currentUser.Id && !input.isActive.Value)
                throw ApiException.BadRequest("Нельзя деактивировать самого себя");

            if (!input.isActive.Value && user.isAdmin)
            {
                var activeAdminCount = await _userManager.Users.CountAsync(u => u.isActive && u.isAdmin);
                if (activeAdminCount <= 1)
                    throw ApiException.BadRequest("Нельзя деактивировать последнего активного администратора");
            }

            user.isActive = input.isActive.Value;
            if (!user.isActive)
                user.selectedStoreId = null;
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw ApiException.BadRequest(result.Errors.Select(e => e.Description).ToArray());

        if (input.password != null)
        {
            if (string.IsNullOrWhiteSpace(input.password))
                throw ApiException.BadRequest("Пароль не может быть пустым");

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, resetToken, input.password);
            if (!passwordResult.Succeeded)
                throw ApiException.BadRequest(passwordResult.Errors.Select(e => e.Description).ToArray());
        }

        return Ok(user.MapToDTO(_serviceProvider));
    }

    [HttpGet]
    [Route("/users/me/memberships")]
    public async Task<IActionResult> GetMyMemberships()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        var storeRepo = _unitOfWork.Repository<Store>();
        var stores = await storeRepo.FindAllAsync(
            s => s.members.Any(m => m.Id == user.Id && m.isActive),
            query => query.Include(s => s.members).Include(s => s.storeAdmins));

        var result = stores.Select(s =>
        {
            var dto = s.MapToDTO(_serviceProvider);
            dto.isStoreAdmin = s.storeAdmins.Any(a => a.Id == user.Id && a.isActive);
            return dto;
        }).ToList();
        return Ok(result);
    }

    [HttpPatch]
    [Route("/users/me")]
    public async Task<IActionResult> PatchMe([FromBody] UpdateMeForm input)
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null)
            throw ApiException.Unauthorized();

        var user = await _userManager.FindByIdAsync(currentUser.Id.ToString());
        if (user == null)
            throw ApiException.Unauthorized();

        if (input.password != null)
        {
            if (string.IsNullOrWhiteSpace(input.currentPassword))
                throw ApiException.BadRequest("Введите текущий пароль");

            if (!await _userManager.CheckPasswordAsync(user, input.currentPassword))
                throw ApiException.BadRequest("Текущий пароль указан неверно");

            if (string.IsNullOrWhiteSpace(input.password))
                throw ApiException.BadRequest("Новый пароль не может быть пустым");
        }

        if (input.userName != null)
        {
            var normalizedName = input.userName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw ApiException.BadRequest("Имя пользователя не может быть пустым");

            if (normalizedName.Length > Delegat.Models.User.USER_NAME_MAX_LENGTH)
                throw ApiException.BadRequest($"Имя пользователя не может быть длиннее {Delegat.Models.User.USER_NAME_MAX_LENGTH} символов");

            var duplicate = await _userManager.FindByNameAsync(normalizedName);
            if (duplicate != null && duplicate.Id != user.Id)
                throw ApiException.BadRequest("Пользователь с таким именем уже существует");

            user.UserName = normalizedName;
            user.NormalizedUserName = _userManager.NormalizeName(normalizedName);
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw ApiException.BadRequest(result.Errors.Select(e => e.Description).ToArray());

        if (input.password != null)
        {
            var passwordResult = await _userManager.ChangePasswordAsync(user, input.currentPassword!, input.password!);
            if (!passwordResult.Succeeded)
                throw ApiException.BadRequest(passwordResult.Errors.Select(e => e.Description).ToArray());
        }

        return Ok(user.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/users/me/memberships/{id}/select")]
    public async Task<IActionResult> PostSelectStore(int id)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null)
            throw ApiException.Unauthorized();

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesAsync(id, query => query.Include(s => s.members));

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        if (!store.members.Any(m => m.Id == user.Id))
            throw ApiException.Forbidden("Вы не являетесь участником этого магазина");

        var userRepo = _unitOfWork.Repository<User>();
        user.UpdateFill(new User.SelectStoreForm(id));
        await userRepo.UpdateAsync(user);
        await _unitOfWork.SaveChangesAsync();

        // Set selected store in cookie
        var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
        Response.Cookies.Append("selected_store_id", id.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = isProduction,
            SameSite = SameSiteMode.Lax,
            Expires = DateTime.UtcNow.AddDays(30)
        });

        return Ok(new { next = "/products" });
    }

    public struct UpdateUserForm
    {
        public string? userName { get; set; }
        public string? password { get; set; }
        public bool? isAdmin { get; set; }
        public bool? isActive { get; set; }
    }

    public struct UpdateMeForm
    {
        public string? userName { get; set; }
        public string? currentPassword { get; set; }
        public string? password { get; set; }
    }
}
