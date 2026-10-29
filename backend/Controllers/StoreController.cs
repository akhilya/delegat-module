using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Services;
using static Delegat.Models.Store;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class StoreController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;
    private readonly IAuthorizationService _authorizationService;
    private readonly IStoreDeletionService _storeDeletion;
    private readonly IBrandingSettingsService _brandingSettings;

    public StoreController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IAuthorizationService authorizationService,
        IStoreDeletionService storeDeletion,
        IBrandingSettingsService brandingSettings)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _authorizationService = authorizationService;
        _storeDeletion = storeDeletion;
        _brandingSettings = brandingSettings;
    }

    [HttpGet]
    [Route("/stores/")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllStores(int startIndex = 0, int pageSize = 100)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var stores = await storeRepo.FindAllAsync(
            s => true,
            q => q.Include(s => s.members)
        );

        var paged = stores
            .Skip(startIndex)
            .Take(pageSize)
            .Select(s => s.MapToDTO(_serviceProvider))
            .ToList();

        return Ok(new
        {
            items = paged,
            total = stores.Count()
        });
    }

    [HttpGet]
    [Route("/stores/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStoreById(int id)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        return Ok(store.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/stores/")]
    [RequiresPermission(Permissions.StoresCreate)]
    public async Task<IActionResult> PostStore([FromBody] AddForm input)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null || !user.isActive)
            throw ApiException.Unauthorized(code: "user_not_found");

        var storeRepo = _unitOfWork.Repository<Store>();
        var name = (input.name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            throw ApiException.BadRequest(code: "store_name_required");
        if (name.Length > Store.STORE_NAME_MAX_LENGTH)
            throw ApiException.BadRequest(code: "store_name_too_long");

        input.name = name;
        var existingStore = await storeRepo.FindAsync(s => s.name == name);

        if (existingStore != null)
            throw ApiException.BadRequest(code: "store_name_taken");

        if (!string.IsNullOrWhiteSpace(input.timezone)
            && !StoreTimeHelper.IsValidTimezoneId(input.timezone.Trim()))
            throw ApiException.BadRequest(code: "timezone_invalid");

        if (!string.IsNullOrWhiteSpace(input.baseCurrency))
        {
            var normalized = input.baseCurrency.Trim().ToUpperInvariant();
            if (!CurrencyService.IsValidCurrencyCode(normalized))
                throw ApiException.BadRequest(code: "currency_code_invalid");
        }

        var newStore = new Store();
        newStore.AddFill(input);

        var userRepo = _unitOfWork.Repository<User>();
        if (input.addAllUsers)
        {
            var activeUsers = await userRepo.QueryTracking()
                .Where(u => u.isActive)
                .ToListAsync();

            foreach (var member in activeUsers.DistinctBy(u => u.Id))
                newStore.members.Add(member);
        }
        else
        {
            var creator = await userRepo.GetByIdTrackingAsync(user.Id);
            if (creator == null || !creator.isActive)
                throw ApiException.Unauthorized(code: "user_not_found");

            newStore.members.Add(creator);
        }

        await storeRepo.AddWithoutSaveAsync(newStore);
        if (!input.fromSetup)
            newStore.onboardingCompletedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return Ok(newStore.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/stores/{id}/members/")]
    [RequiresPermission(Permissions.StoreMembersManage, requireStoreMembership: true)]
    public async Task<IActionResult> PostAddStoreMember(int id, [FromBody] AddMemberForm input)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        if (store.members.Any(m => m.Id == input.userId))
            throw ApiException.BadRequest(code: "user_already_store_member");

        var userRepo = _unitOfWork.Repository<User>();
        var inviteUser = await userRepo.GetByIdTrackingAsync(input.userId);

        if (inviteUser == null || !inviteUser.isActive)
            throw ApiException.NotFound(code: "user_not_found");

        store.members.Add(inviteUser);
        await _unitOfWork.SaveChangesAsync();

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpDelete]
    [Route("/stores/{id}/members/{userId}")]
    [RequiresPermission(Permissions.StoreMembersManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteStoreMember(int id, int userId)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        var memberToRemove = store.members.FirstOrDefault(m => m.Id == userId);
        if (memberToRemove == null)
            throw ApiException.BadRequest(code: "user_not_store_member");

        store.members.Remove(memberToRemove);

        if (memberToRemove.selectedStoreId == id)
            memberToRemove.selectedStoreId = null;

        await _unitOfWork.SaveChangesAsync();

        if (_currentUserService.GetUserId() == userId)
            Response.Cookies.Delete("selected_store_id");

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpGet]
    [Route("/stores/{id}/detail")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetStoreDetail(int id)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpPatch]
    [Route("/stores/{id}")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    public async Task<IActionResult> PatchStore(int id, [FromBody] UpdateStoreForm input)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        var nameChanged = false;
        if (input.name != null)
        {
            var name = input.name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw ApiException.BadRequest(code: "store_name_required");

            if (name.Length > Store.STORE_NAME_MAX_LENGTH)
                throw ApiException.BadRequest(code: "store_name_too_long");

            var duplicate = await storeRepo.FindAsync(s => s.Id != id && s.name == name);
            if (duplicate != null)
                throw ApiException.BadRequest(code: "store_name_taken");

            if (!string.Equals(store.name, name, StringComparison.Ordinal))
            {
                store.name = name;
                nameChanged = true;
            }
        }

        if (input.memberIds != null)
        {
            var membersAuth = await _authorizationService.AuthorizeAsync(
                User, PermissionPolicyProvider.StorePolicyName(Permissions.StoreMembersManage));
            if (!membersAuth.Succeeded)
                throw ApiException.Forbidden(code: "store_members_manage_forbidden");

            var memberIds = input.memberIds.Distinct().ToHashSet();
            var removedMembers = store.members.Where(member => !memberIds.Contains(member.Id)).ToList();
            var users = await _unitOfWork.Repository<User>()
                .QueryTracking()
                .Where(u => memberIds.Contains(u.Id) && u.isActive)
                .ToListAsync();

            if (users.Count != memberIds.Count)
                throw ApiException.BadRequest(code: "user_not_found");

            store.members.Clear();
            foreach (var user in users)
                store.members.Add(user);

            foreach (var removedMember in removedMembers)
            {
                if (removedMember.selectedStoreId == id)
                    removedMember.selectedStoreId = null;
            }

            var currentUserId = _currentUserService.GetUserId();
            if (currentUserId != null && removedMembers.Any(m => m.Id == currentUserId.Value))
            {
                Response.Cookies.Delete("selected_store_id");
            }
        }

        // Null/whitespace = leave unchanged (partial PATCH; e.g. members-only edit when CP catalog is empty).
        if (!string.IsNullOrWhiteSpace(input.timezone))
        {
            var tz = input.timezone.Trim();
            if (!StoreTimeHelper.IsValidTimezoneId(tz))
                throw ApiException.BadRequest(code: "timezone_invalid");
            store.timezone = tz;
        }

        if (input.extendedPriceParameters.HasValue)
            store.extendedPriceParameters = input.extendedPriceParameters.Value;

        await _unitOfWork.SaveChangesAsync();

        if (nameChanged)
            _brandingSettings.InvalidateStoreCache(id);

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpGet]
    [Route("/stores/{id}/currencies")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    public async Task<IActionResult> GetStoreCurrencies(int id, CancellationToken ct)
    {
        var store = await _unitOfWork.Repository<Store>().GetByIdWithIncludesAsync(id, q => q.Include(s => s.currencies));
        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        return Ok(store.MapCurrencyDtos());
    }

    [HttpPost]
    [Route("/stores/{id}/currencies")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    public async Task<IActionResult> AddStoreCurrency(int id, [FromBody] AddStoreCurrencyForm input, CancellationToken ct)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, q => q.Include(s => s.currencies));
        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        var code = CurrencyService.NormalizeCode(input.code);
        var baseCurrency = CurrencyService.NormalizeCode(store.baseCurrency);
        if (code == baseCurrency)
            throw ApiException.BadRequest(code: "currency_is_base");
        if (!CurrencyService.IsValidCurrencyCode(code))
            throw ApiException.BadRequest(code: "currency_code_invalid");
        if (input.rateToBase <= 0)
            throw ApiException.BadRequest(code: "currency_rate_invalid");

        if (store.currencies.Any(c => c.code == code))
            throw ApiException.BadRequest(code: "currency_already_added");

        store.currencies.Add(new StoreCurrency
        {
            storeId = id,
            code = code,
            rateToBase = input.rateToBase,
            isEnabled = true,
            updatedAt = DateTime.UtcNow
        });
        await _unitOfWork.SaveChangesAsync(ct);

        return Ok(store.MapCurrencyDtos());
    }

    [HttpPatch]
    [Route("/stores/{id}/currencies/{code}")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    public async Task<IActionResult> PatchStoreCurrency(int id, string code, [FromBody] PatchStoreCurrencyForm input, CancellationToken ct)
    {
        code = CurrencyService.NormalizeCode(code);

        var store = await _unitOfWork.Repository<Store>().GetByIdWithIncludesTrackingAsync(id, q => q.Include(s => s.currencies));
        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        var baseCurrency = CurrencyService.NormalizeCode(store.baseCurrency);
        if (code == baseCurrency)
            throw ApiException.BadRequest(code: "base_currency_immutable");

        var currency = store.currencies.FirstOrDefault(c => c.code == code);
        if (currency == null)
            throw ApiException.NotFound(code: "currency_not_found");

        if (input.rateToBase.HasValue)
        {
            if (input.rateToBase.Value <= 0)
                throw ApiException.BadRequest(code: "currency_rate_invalid");
            currency.rateToBase = input.rateToBase.Value;
            currency.updatedAt = DateTime.UtcNow;
        }
        if (input.isEnabled.HasValue)
            currency.isEnabled = input.isEnabled.Value;

        await _unitOfWork.SaveChangesAsync(ct);
        return Ok(store.MapCurrencyDtos());
    }

    [HttpDelete]
    [Route("/stores/{id}/currencies/{code}")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    public async Task<IActionResult> DeleteStoreCurrency(int id, string code, CancellationToken ct)
    {
        code = CurrencyService.NormalizeCode(code);

        var store = await _unitOfWork.Repository<Store>().GetByIdWithIncludesTrackingAsync(id, q => q.Include(s => s.currencies));
        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        var baseCurrency = CurrencyService.NormalizeCode(store.baseCurrency);
        if (code == baseCurrency)
            throw ApiException.BadRequest(code: "base_currency_immutable");

        var currency = store.currencies.FirstOrDefault(c => c.code == code);
        if (currency == null)
            throw ApiException.NotFound(code: "currency_not_found");

        store.currencies.Remove(currency);
        await _unitOfWork.SaveChangesAsync(ct);
        return Ok(store.MapCurrencyDtos());
    }

    [HttpGet]
    [Route("/users/all")]
    [RequiresPermission(Permissions.UsersManage)]
    public async Task<IActionResult> GetAllUsers()
    {
        var userRepo = _unitOfWork.Repository<User>();
        var users = await userRepo.GetAllAsync(u => u.isActive);

        return Ok(users.Select(u => new { id = u.Id, userName = u.UserName }).ToList());
    }

    [HttpDelete]
    [Route("/stores/{id}")]
    public async Task<IActionResult> DeleteStore(int id, [FromBody] DeleteStoreForm input)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null || !user.isActive)
            throw ApiException.Unauthorized(code: "user_not_found");
        if (!user.isAdmin)
            throw ApiException.Forbidden(code: "store_delete_forbidden");

        await _storeDeletion.DeleteStoreAsync(id, input.confirmationName ?? "");

        if (_currentUserService.GetSelectedStoreId() == id)
            Response.Cookies.Delete("selected_store_id");

        return NoContent();
    }

    /// <summary>Branding for a store (admin store settings).</summary>
    [HttpGet]
    [Route("/stores/{id}/branding")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    public async Task<ActionResult<BrandingSettingsDto>> GetStoreBranding(int id, CancellationToken ct)
    {
        await EnsureStoreExistsAsync(id);
        return Ok(await _brandingSettings.GetForStoreAsync(id, ct));
    }

    [HttpPut]
    [Route("/stores/{id}/branding")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    public async Task<ActionResult<BrandingSettingsDto>> PutStoreBranding(
        int id,
        [FromBody] UpdateBrandingSettingsRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw ApiException.BadRequest(code: "request_body_required");

        await EnsureStoreExistsAsync(id);
        return Ok(await _brandingSettings.UpdateAsync(id, request, ct));
    }

    [HttpPost]
    [Route("/stores/{id}/branding/logo")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    [RequestSizeLimit(BrandingSettingsService.MaxUploadBytes)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadStoreBrandingLogo(
        int id,
        IFormFile file,
        CancellationToken ct)
    {
        await EnsureStoreExistsAsync(id);
        return Ok(await _brandingSettings.UploadLogoAsync(id, file, ct));
    }

    [HttpPost]
    [Route("/stores/{id}/branding/favicon")]
    [RequiresPermission(Permissions.StoreSettingsManage, requireStoreMembership: true)]
    [RequestSizeLimit(BrandingSettingsService.MaxUploadBytes)]
    public async Task<ActionResult<BrandingSettingsDto>> UploadStoreBrandingFavicon(
        int id,
        IFormFile file,
        CancellationToken ct)
    {
        await EnsureStoreExistsAsync(id);
        return Ok(await _brandingSettings.UploadFaviconAsync(id, file, ct));
    }

    /// <summary>
    /// Marks first-run Setup as finished for this store.
    /// </summary>
    [HttpPost]
    [Route("/stores/{id}/onboarding/complete")]
    [RequiresPermission(Permissions.StoresCreate, requireStoreMembership: true)]
    public async Task<IActionResult> CompleteOnboarding(int id)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdTrackingAsync(id);
        if (store == null)
            throw ApiException.NotFound(code: "store_not_found");

        store.onboardingCompletedAt ??= DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return Ok(store.MapToDTO(_serviceProvider));
    }

    public struct DeleteStoreForm
    {
        public string confirmationName { get; set; }
    }

    public struct AddMemberForm
    {
        public int userId { get; set; }
    }

    public struct UpdateStoreForm
    {
        public string? name { get; set; }
        public List<int>? memberIds { get; set; }
        public string? timezone { get; set; }
        public bool? extendedPriceParameters { get; set; }
    }

    public struct AddStoreCurrencyForm
    {
        public string code { get; set; }
        public decimal rateToBase { get; set; }
    }

    public struct PatchStoreCurrencyForm
    {
        public decimal? rateToBase { get; set; }
        public bool? isEnabled { get; set; }
    }

    private async Task EnsureStoreExistsAsync(int id)
    {
        var exists = await _unitOfWork.Repository<Store>().FindAsync(s => s.Id == id);
        if (exists == null)
            throw ApiException.NotFound(code: "store_not_found");
    }

    private static IQueryable<Store> StoreIncludes(IQueryable<Store> query) =>
        query.Include(s => s.members).Include(s => s.currencies);
}
