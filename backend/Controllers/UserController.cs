using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;

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
    private readonly IBrandingSettingsService _brandingSettings;

    public UserController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        UserManager<User> userManager,
        IPermissionService permissionService,
        IBrandingSettingsService brandingSettings)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _userManager = userManager;
        _permissionService = permissionService;
        _brandingSettings = brandingSettings;
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
            throw ApiException.NotFound(code: "user_not_found");

        if (input.userName != null)
        {
            var normalizedName = input.userName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw ApiException.BadRequest(code: "username_required");

            if (normalizedName.Length > Delegat.Models.User.USER_NAME_MAX_LENGTH)
                throw ApiException.BadRequest(code: "username_too_long");

            var duplicate = await _userManager.FindByNameAsync(normalizedName);
            if (duplicate != null && duplicate.Id != user.Id)
                throw ApiException.BadRequest(code: "username_taken");

            user.UserName = normalizedName;
            user.NormalizedUserName = _userManager.NormalizeName(normalizedName);
        }

        if (input.isAdmin.HasValue)
        {
            if (!currentUser.isAdmin)
                throw ApiException.Forbidden(code: "owner_flag_change_forbidden");

            if (user.Id == currentUser.Id && !input.isAdmin.Value)
                throw ApiException.BadRequest(code: "owner_self_demote_forbidden");

            if (!input.isAdmin.Value)
            {
                var activeAdminCount = await _userManager.Users.CountAsync(u => u.isActive && u.isAdmin, ct);
                if (user.isAdmin && activeAdminCount <= 1)
                    throw ApiException.BadRequest(code: "last_owner_required");
            }

            user.isAdmin = input.isAdmin.Value;
            if (user.isAdmin)
                await _permissionService.EnsureOwnerAccessAsync(user, ct);
        }

        if (input.isActive.HasValue)
        {
            if (user.Id == currentUser.Id && !input.isActive.Value)
                throw ApiException.BadRequest(code: "self_deactivation_forbidden");

            if (!input.isActive.Value && user.isAdmin)
            {
                var activeAdminCount = await _userManager.Users.CountAsync(u => u.isActive && u.isAdmin, ct);
                if (activeAdminCount <= 1)
                    throw ApiException.BadRequest(code: "last_active_owner_required");
            }

            user.isActive = input.isActive.Value;
            if (!user.isActive)
                user.selectedStoreId = null;
        }

        var result = await _userManager.UpdateAsync(user);
        result.ThrowIfFailed();

        _permissionService.InvalidateUserAccess(user.Id);

        if (input.password != null)
        {
            if (string.IsNullOrWhiteSpace(input.password))
                throw ApiException.BadRequest(code: "password_required");

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await _userManager.ResetPasswordAsync(user, resetToken, input.password);
            passwordResult.ThrowIfFailed();
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
            throw ApiException.NotFound(code: "user_not_found");

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

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
            throw ApiException.NotFound(code: "user_not_found");

        // Admin-only claims are never assignable - only User.isAdmin receives them at hydrate time.
        var roles = input.roles ?? [];
        var grants = (input.permissionGrants ?? [])
            .Where(p => !Permissions.IsAdminOnly(p))
            .ToList();

        UserAccessMutation.EnsureCanAssign(currentUser, user, roles);

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

        var favicons = await _brandingSettings.GetFaviconUrlsAsync(stores.Select(s => s.Id));
        var result = stores.Select(s =>
        {
            var dto = s.MapToDTO(_serviceProvider);
            dto.faviconUrl = favicons.TryGetValue(s.Id, out var url)
                ? url
                : StoreBrandingSettings.DefaultFaviconUrl;
            return dto;
        }).ToList();
        return Ok(result);
    }

    [HttpPatch]
    [Route("/users/me")]
    public async Task<IActionResult> PatchMe([FromBody] UpdateMeForm input, CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (currentUser == null)
            throw ApiException.Unauthorized();

        CabinetViewOnlyGuard.ThrowIfRestricted(User);

        var user = await _userManager.FindByIdAsync(currentUser.Id.ToString());
        if (user == null)
            throw ApiException.Unauthorized();

        if (input.password != null)
        {
            if (string.IsNullOrWhiteSpace(input.currentPassword))
                throw ApiException.BadRequest(code: "current_password_required");

            if (!await _userManager.CheckPasswordAsync(user, input.currentPassword))
                throw ApiException.BadRequest(code: "password_mismatch");

            if (string.IsNullOrWhiteSpace(input.password))
                throw ApiException.BadRequest(code: "password_required");
        }

        if (input.userName != null)
        {
            var normalizedName = input.userName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw ApiException.BadRequest(code: "username_required");

            if (normalizedName.Length > Delegat.Models.User.USER_NAME_MAX_LENGTH)
                throw ApiException.BadRequest(code: "username_too_long");

            var duplicate = await _userManager.FindByNameAsync(normalizedName);
            if (duplicate != null && duplicate.Id != user.Id)
                throw ApiException.BadRequest(code: "username_taken");

            user.UserName = normalizedName;
            user.NormalizedUserName = _userManager.NormalizeName(normalizedName);
        }

        var result = await _userManager.UpdateAsync(user);
        result.ThrowIfFailed();

        if (input.password != null)
        {
            var passwordResult = await _userManager.ChangePasswordAsync(user, input.currentPassword!, input.password!);
            passwordResult.ThrowIfFailed();
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
            throw ApiException.Forbidden(code: "access_denied_store");

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
