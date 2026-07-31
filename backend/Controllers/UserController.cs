using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class UserController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly UserManager<User> _userManager;
    private readonly IPermissionService _permissionService;

    public UserController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        UserManager<User> userManager,
        IPermissionService permissionService)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _userManager = userManager;
        _permissionService = permissionService;
    }

    [HttpGet]
    [Route("/users/")]
    [RequiresPermission(Permissions.UsersManage)]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        var users = await _userManager.Users
            .OrderByDescending(u => u.isActive)
            .ThenBy(u => u.UserName)
            .ToListAsync(ct);

        var result = new List<object>();
        foreach (var u in users)
            result.Add(await MapUserDtoAsync(u, ct));

        return Ok(result);
    }

    [HttpPatch]
    [Route("/users/{id}")]
    [RequiresPermission(Permissions.UsersManage)]
    public async Task<IActionResult> PatchUser(int id, [FromBody] UpdateUserForm input, CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (currentUser == null)
            throw ApiException.Unauthorized();

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
            throw ApiException.NotFound("User not found", code: "user_not_found");

        if (input.userName != null)
        {
            var normalizedName = input.userName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw ApiException.BadRequest("Username cannot be empty", code: "required_fields_missing");

            if (normalizedName.Length > Delegat.Models.User.USER_NAME_MAX_LENGTH)
                throw ApiException.BadRequest($"Username cannot be longer than {Delegat.Models.User.USER_NAME_MAX_LENGTH} characters");

            var duplicate = await _userManager.FindByNameAsync(normalizedName);
            if (duplicate != null && duplicate.Id != user.Id)
                throw ApiException.BadRequest("A user with this name already exists");

            user.UserName = normalizedName;
            user.NormalizedUserName = _userManager.NormalizeName(normalizedName);
        }

        if (input.isAdmin.HasValue)
        {
            if (!currentUser.isAdmin)
                throw ApiException.Forbidden("Only the instance owner can change the owner flag");

            if (user.Id == currentUser.Id && !input.isAdmin.Value)
                throw ApiException.BadRequest("Cannot remove owner rights from yourself");

            if (!input.isAdmin.Value)
            {
                var activeAdminCount = await _userManager.Users.CountAsync(u => u.isActive && u.isAdmin, ct);
                if (user.isAdmin && activeAdminCount <= 1)
                    throw ApiException.BadRequest("Cannot remove rights from the last active owner");
            }

            user.isAdmin = input.isAdmin.Value;
            if (user.isAdmin)
                await _permissionService.EnsureOwnerAccessAsync(user, ct);
        }

        if (input.isActive.HasValue)
        {
            if (user.Id == currentUser.Id && !input.isActive.Value)
                throw ApiException.BadRequest("Cannot deactivate yourself");

            if (!input.isActive.Value && user.isAdmin)
            {
                var activeAdminCount = await _userManager.Users.CountAsync(u => u.isActive && u.isAdmin, ct);
                if (activeAdminCount <= 1)
                    throw ApiException.BadRequest("Cannot deactivate the last active owner");
            }

            user.isActive = input.isActive.Value;
            if (!user.isActive)
                user.selectedStoreId = null;
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw ApiException.BadRequest(result.Errors.Select(e => e.Description).ToArray());

        _permissionService.InvalidateUserAccess(user.Id);

        if (input.password != null)
        {
            if (string.IsNullOrWhiteSpace(input.password))
                throw ApiException.BadRequest("Password cannot be empty", code: "required_fields_missing");

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, resetToken, input.password);
            if (!passwordResult.Succeeded)
                throw ApiException.BadRequest(passwordResult.Errors.Select(e => e.Description).ToArray());
        }

        return Ok(await MapUserDtoAsync(user, ct));
    }

    [HttpGet]
    [Route("/users/{id}/access")]
    [RequiresPermission(Permissions.RolesManage)]
    public async Task<IActionResult> GetUserAccess(int id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
            throw ApiException.NotFound("User not found", code: "user_not_found");

        var access = await _permissionService.GetUserAccessAsync(id, ct);
        return Ok(access);
    }

    [HttpPut]
    [Route("/users/{id}/access")]
    [RequiresPermission(Permissions.RolesManage)]
    public async Task<IActionResult> PutUserAccess(int id, [FromBody] SetUserAccessForm input, CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (currentUser == null)
            throw ApiException.Unauthorized();

        // roles.manage is never assignable - only User.isAdmin receives it at hydrate time.
        var roles = input.roles ?? [];
        var grants = (input.permissionGrants ?? [])
            .Where(p => !Permissions.IsAdminOnly(p))
            .ToList();
        if (!currentUser.isAdmin)
        {
            if (roles.Contains(AppRoles.Owner, StringComparer.OrdinalIgnoreCase))
                throw ApiException.Forbidden("Only the instance owner can assign the Owner role");
        }

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
            throw ApiException.NotFound("User not found", code: "user_not_found");

        await _permissionService.SetUserAccessAsync(id, roles, grants, ct);
        return Ok(await _permissionService.GetUserAccessAsync(id, ct));
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
            query => query.Include(s => s.members).Include(s => s.currencies));

        var result = stores.Select(s => s.MapToDTO(_serviceProvider)).ToList();
        return Ok(result);
    }

    [HttpPatch]
    [Route("/users/me")]
    public async Task<IActionResult> PatchMe([FromBody] UpdateMeForm input, CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (currentUser == null)
            throw ApiException.Unauthorized();

        var user = await _userManager.FindByIdAsync(currentUser.Id.ToString());
        if (user == null)
            throw ApiException.Unauthorized();

        if (input.password != null)
        {
            if (string.IsNullOrWhiteSpace(input.currentPassword))
                throw ApiException.BadRequest("Enter your current password", code: "required_fields_missing");

            if (!await _userManager.CheckPasswordAsync(user, input.currentPassword))
                throw ApiException.BadRequest("Current password is incorrect", code: "invalid_credentials");

            if (string.IsNullOrWhiteSpace(input.password))
                throw ApiException.BadRequest("New password cannot be empty", code: "required_fields_missing");
        }

        if (input.userName != null)
        {
            var normalizedName = input.userName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw ApiException.BadRequest("Username cannot be empty", code: "required_fields_missing");

            if (normalizedName.Length > Delegat.Models.User.USER_NAME_MAX_LENGTH)
                throw ApiException.BadRequest($"Username cannot be longer than {Delegat.Models.User.USER_NAME_MAX_LENGTH} characters");

            var duplicate = await _userManager.FindByNameAsync(normalizedName);
            if (duplicate != null && duplicate.Id != user.Id)
                throw ApiException.BadRequest("A user with this name already exists");

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

        return Ok(await MapUserDtoAsync(user, ct));
    }

    [HttpPost]
    [Route("/users/me/memberships/{id}/select")]
    public async Task<IActionResult> PostSelectStore(int id, CancellationToken ct)
    {
        var userId = _currentUserService.GetUserId()
            ?? throw ApiException.Unauthorized();

        // Membership check without Include(members) - avoids attaching a second User instance
        // when Identity already tracks the current user (e.g. after OnTokenValidated hydrate).
        var isMember = await _unitOfWork.Repository<Store>().Query()
            .Where(s => s.Id == id)
            .SelectMany(s => s.members)
            .AnyAsync(m => m.Id == userId && m.isActive, ct);

        if (!isMember)
            throw ApiException.Forbidden("You are not a member of this store", code: "access_denied_store");

        // Update via UserManager so we mutate the already-tracked Identity user, if any.
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null || !user.isActive)
            throw ApiException.Unauthorized();

        user.selectedStoreId = id;
        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
            throw ApiException.BadRequest(update.Errors.Select(e => e.Description).ToArray());

        Response.Cookies.Append(
            "selected_store_id",
            id.ToString(),
            AuthCookie.SelectedStore(Request));

        return Ok(new { next = "/products" });
    }

    private async Task<Delegat.Models.User.UserDTO> MapUserDtoAsync(Delegat.Models.User user, CancellationToken ct)
    {
        var dto = user.MapToDTO(_serviceProvider);
        var access = await _permissionService.GetUserAccessAsync(user.Id, ct);
        dto.roles = access.Roles;
        dto.permissions = access.EffectivePermissions;
        return dto;
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

    public sealed class SetUserAccessForm
    {
        public List<string>? roles { get; set; }
        public List<string>? permissionGrants { get; set; }
    }
}
