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

    public StoreController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("/stores/")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllStores(int startIndex = 0, int pageSize = 100)
    {
        var storeRepo = _unitOfWork.Repository<Store>();
        var stores = await storeRepo.FindAllAsync(
            s => true,
            q => q.Include(s => s.members).Include(s => s.storeAdmins)
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
            throw ApiException.NotFound("Store not found");

        return Ok(store.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/stores/")]
    public async Task<IActionResult> PostStore([FromBody] AddForm input)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null || !user.isActive || !user.isAdmin)
            throw ApiException.Forbidden("Only administrators can create stores");

        var storeRepo = _unitOfWork.Repository<Store>();
        var existingStore = await storeRepo.FindAsync(s => s.name == input.name);

        if (existingStore != null)
            throw ApiException.BadRequest("A store with this name already exists");

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

            var creator = activeUsers.FirstOrDefault(u => u.Id == user.Id);
            if (creator != null)
                newStore.storeAdmins.Add(creator);
        }
        else
        {
            var creator = await userRepo.GetByIdTrackingAsync(user.Id);
            if (creator == null || !creator.isActive)
                throw ApiException.Unauthorized("User not found", code: "user_not_found");

            newStore.members.Add(creator);
            newStore.storeAdmins.Add(creator);
        }

        await storeRepo.AddWithoutSaveAsync(newStore);
        await _unitOfWork.SaveChangesAsync();

        return Ok(newStore.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/stores/{id}/members/")]
    public async Task<IActionResult> PostAddStoreMember(int id, [FromBody] AddMemberForm input)
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Only administrators can add members");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound("Store not found");

        if (store.members.Any(m => m.Id == input.userId))
            throw ApiException.BadRequest("User is already a member");

        var userRepo = _unitOfWork.Repository<User>();
        var inviteUser = await userRepo.GetByIdTrackingAsync(input.userId);

        if (inviteUser == null || !inviteUser.isActive)
            throw ApiException.NotFound("User not found", code: "user_not_found");

        store.members.Add(inviteUser);
        await _unitOfWork.SaveChangesAsync();

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpDelete]
    [Route("/stores/{id}/members/{userId}")]
    public async Task<IActionResult> DeleteStoreMember(int id, int userId)
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Only administrators can remove members");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound("Store not found");

        var memberToRemove = store.members.FirstOrDefault(m => m.Id == userId);
        if (memberToRemove == null)
            throw ApiException.BadRequest("User is not a member of this store");

        store.members.Remove(memberToRemove);
        var adminToRemove = store.storeAdmins.FirstOrDefault(m => m.Id == userId);
        if (adminToRemove != null)
            store.storeAdmins.Remove(adminToRemove);

        // Clear selected store if user was removed from their selected store
        if (memberToRemove.selectedStoreId == id)
        {
            memberToRemove.selectedStoreId = null;
        }

        await _unitOfWork.SaveChangesAsync();

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpGet]
    [Route("/stores/{id}/detail")]
    public async Task<IActionResult> GetStoreDetail(int id)
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Only administrators can view store details");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound("Store not found");

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpPatch]
    [Route("/stores/{id}")]
    public async Task<IActionResult> PatchStore(int id, [FromBody] UpdateStoreForm input)
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Insufficient permissions to modify the store");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound("Store not found");

        if (input.name != null)
        {
            var name = input.name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw ApiException.BadRequest("Store name cannot be empty", code: "required_fields_missing");

            if (name.Length > Store.STORE_NAME_MAX_LENGTH)
                throw ApiException.BadRequest($"Store name cannot be longer than {Store.STORE_NAME_MAX_LENGTH} characters");

            var duplicate = await storeRepo.FindAsync(s => s.Id != id && s.name == name);
            if (duplicate != null)
                throw ApiException.BadRequest("A store with this name already exists");

            store.name = name;
        }

        var isPlatformAdmin = currentUser?.isAdmin == true;
        if ((input.memberIds != null || input.storeAdminIds != null) && !isPlatformAdmin)
            throw ApiException.Forbidden("Only a platform administrator can change store members and admins");

        if (input.memberIds != null)
        {
            var memberIds = input.memberIds.Distinct().ToHashSet();
            var removedMembers = store.members.Where(member => !memberIds.Contains(member.Id)).ToList();
            var users = await _unitOfWork.Repository<User>()
                .QueryTracking()
                .Where(u => memberIds.Contains(u.Id) && u.isActive)
                .ToListAsync();

            if (users.Count != memberIds.Count)
                throw ApiException.BadRequest("One or more users not found", code: "user_not_found");

            store.members.Clear();
            foreach (var user in users)
                store.members.Add(user);

            foreach (var removedMember in removedMembers)
            {
                if (removedMember.selectedStoreId == id)
                    removedMember.selectedStoreId = null;
            }

            var removedStoreAdmins = store.storeAdmins.Where(admin => !memberIds.Contains(admin.Id)).ToList();
            foreach (var admin in removedStoreAdmins)
                store.storeAdmins.Remove(admin);
        }

        if (input.storeAdminIds != null)
        {
            var adminIds = input.storeAdminIds.Distinct().ToHashSet();
            var memberIds = store.members.Select(m => m.Id).ToHashSet();
            if (!adminIds.All(memberIds.Contains))
                throw ApiException.BadRequest("A store administrator must be a member of this store");

            var admins = store.members.Where(m => adminIds.Contains(m.Id)).ToList();
            store.storeAdmins.Clear();
            foreach (var admin in admins)
                store.storeAdmins.Add(admin);
        }

        if (input.timezone != null)
        {
            var tz = input.timezone.Trim();
            if (!StoreTimeHelper.IsValidTimezoneId(tz))
                throw ApiException.BadRequest("Invalid timezone");
            store.timezone = tz;
        }

        if (input.extendedPriceParameters.HasValue)
            store.extendedPriceParameters = input.extendedPriceParameters.Value;

        await _unitOfWork.SaveChangesAsync();

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpGet]
    [Route("/stores/{id}/currencies")]
    public async Task<IActionResult> GetStoreCurrencies(int id, CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Insufficient permissions", code: "access_denied_store");

        var store = await _unitOfWork.Repository<Store>().GetByIdWithIncludesAsync(id, q => q.Include(s => s.currencies));
        if (store == null)
            throw ApiException.NotFound("Store not found");

        return Ok(store.MapCurrencyDtos());
    }

    [HttpPost]
    [Route("/stores/{id}/currencies")]
    public async Task<IActionResult> AddStoreCurrency(int id, [FromBody] AddStoreCurrencyForm input, CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Insufficient permissions", code: "access_denied_store");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, q => q.Include(s => s.currencies));
        if (store == null)
            throw ApiException.NotFound("Store not found");

        var code = CurrencyService.NormalizeCode(input.code);
        var baseCurrency = CurrencyService.NormalizeCode(store.baseCurrency);
        if (code == baseCurrency)
            throw ApiException.BadRequest($"{baseCurrency} is the base currency, no need to add it");
        if (!CurrencyService.IsValidCurrencyCode(code))
            throw ApiException.BadRequest("Currency code must be 3 letters");
        if (input.rateToBase <= 0)
            throw ApiException.BadRequest("Rate must be greater than zero");

        if (store.currencies.Any(c => c.code == code))
            throw ApiException.BadRequest("Currency has already been added");

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
    public async Task<IActionResult> PatchStoreCurrency(int id, string code, [FromBody] PatchStoreCurrencyForm input, CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Insufficient permissions", code: "access_denied_store");

        code = CurrencyService.NormalizeCode(code);

        var store = await _unitOfWork.Repository<Store>().GetByIdWithIncludesTrackingAsync(id, q => q.Include(s => s.currencies));
        if (store == null)
            throw ApiException.NotFound("Store not found");

        var baseCurrency = CurrencyService.NormalizeCode(store.baseCurrency);
        if (code == baseCurrency)
            throw ApiException.BadRequest($"The base currency {baseCurrency} cannot be changed");

        var currency = store.currencies.FirstOrDefault(c => c.code == code);
        if (currency == null)
            throw ApiException.NotFound("Currency not found");

        if (input.rateToBase.HasValue)
        {
            if (input.rateToBase.Value <= 0)
                throw ApiException.BadRequest("Rate must be greater than zero");
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
    public async Task<IActionResult> DeleteStoreCurrency(int id, string code, CancellationToken ct)
    {
        var currentUser = await _currentUserService.GetUserAsync(ct);
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Insufficient permissions", code: "access_denied_store");

        code = CurrencyService.NormalizeCode(code);

        var store = await _unitOfWork.Repository<Store>().GetByIdWithIncludesTrackingAsync(id, q => q.Include(s => s.currencies));
        if (store == null)
            throw ApiException.NotFound("Store not found");

        var baseCurrency = CurrencyService.NormalizeCode(store.baseCurrency);
        if (code == baseCurrency)
            throw ApiException.BadRequest($"The base currency {baseCurrency} cannot be removed");

        var currency = store.currencies.FirstOrDefault(c => c.code == code);
        if (currency == null)
            throw ApiException.NotFound("Currency not found");

        store.currencies.Remove(currency);
        await _unitOfWork.SaveChangesAsync(ct);
        return Ok(store.MapCurrencyDtos());
    }

    [HttpGet]
    [Route("/users/all")]
    public async Task<IActionResult> GetAllUsers()
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || !currentUser.isActive || !currentUser.isAdmin)
            throw ApiException.Forbidden("Only administrators can view all users");

        var userRepo = _unitOfWork.Repository<User>();
        var users = await userRepo.GetAllAsync(u => u.isActive);

        return Ok(users.Select(u => new { id = u.Id, userName = u.UserName }).ToList());
    }

    public struct AddMemberForm
    {
        public int userId { get; set; }
    }

    public struct UpdateStoreForm
    {
        public string? name { get; set; }
        public List<int>? memberIds { get; set; }
        public List<int>? storeAdminIds { get; set; }
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

    private static IQueryable<Store> StoreIncludes(IQueryable<Store> query) =>
        query.Include(s => s.members).Include(s => s.storeAdmins).Include(s => s.currencies);

    private async Task<bool> CanManageStoreAsync(int storeId, User? user)
    {
        if (user == null || !user.isActive)
            return false;

        if (user.isAdmin)
            return true;

        return await _unitOfWork.Repository<Store>()
            .Query()
            .AnyAsync(s => s.Id == storeId && s.storeAdmins.Any(a => a.Id == user.Id && a.isActive));
    }
}
