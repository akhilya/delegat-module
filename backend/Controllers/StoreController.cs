using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Auth;
using PrintO.Infrastructure.Data;
using PrintO.Infrastructure.Middleware;
using PrintO.Models;
using static PrintO.Models.Store;

namespace PrintO.Controllers;

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
            throw ApiException.NotFound("Магазин не найден");

        return Ok(store.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/stores/")]
    public async Task<IActionResult> PostStore([FromBody] AddForm input)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null || !user.isActive || !user.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут создавать магазины");

        var storeRepo = _unitOfWork.Repository<Store>();
        var existingStore = await storeRepo.FindAsync(s => s.name == input.name);

        if (existingStore != null)
            throw ApiException.BadRequest("Магазин с таким именем уже существует");

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
                throw ApiException.Unauthorized("Пользователь не найден");

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
            throw ApiException.Forbidden("Только администраторы могут добавлять участников");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        if (store.members.Any(m => m.Id == input.userId))
            throw ApiException.BadRequest("Пользователь уже является участником");

        var userRepo = _unitOfWork.Repository<User>();
        var inviteUser = await userRepo.GetByIdTrackingAsync(input.userId);

        if (inviteUser == null || !inviteUser.isActive)
            throw ApiException.NotFound("Пользователь не найден");

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
            throw ApiException.Forbidden("Только администраторы могут удалять участников");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        var memberToRemove = store.members.FirstOrDefault(m => m.Id == userId);
        if (memberToRemove == null)
            throw ApiException.BadRequest("Пользователь не является участником этого магазина");

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
            throw ApiException.Forbidden("Только администраторы могут просматривать детали магазина");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpPatch]
    [Route("/stores/{id}")]
    public async Task<IActionResult> PatchStore(int id, [FromBody] UpdateStoreForm input)
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (!await CanManageStoreAsync(id, currentUser))
            throw ApiException.Forbidden("Недостаточно прав для изменения магазина");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, StoreIncludes);

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        if (input.name != null)
        {
            var name = input.name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw ApiException.BadRequest("Название магазина не может быть пустым");

            if (name.Length > Store.STORE_NAME_MAX_LENGTH)
                throw ApiException.BadRequest($"Название магазина не может быть длиннее {Store.STORE_NAME_MAX_LENGTH} символов");

            var duplicate = await storeRepo.FindAsync(s => s.Id != id && s.name == name);
            if (duplicate != null)
                throw ApiException.BadRequest("Магазин с таким именем уже существует");

            store.name = name;
        }

        var isPlatformAdmin = currentUser?.isAdmin == true;
        if ((input.memberIds != null || input.storeAdminIds != null) && !isPlatformAdmin)
            throw ApiException.Forbidden("Только администратор платформы может менять участников и администраторов магазина");

        if (input.memberIds != null)
        {
            var memberIds = input.memberIds.Distinct().ToHashSet();
            var removedMembers = store.members.Where(member => !memberIds.Contains(member.Id)).ToList();
            var users = await _unitOfWork.Repository<User>()
                .QueryTracking()
                .Where(u => memberIds.Contains(u.Id) && u.isActive)
                .ToListAsync();

            if (users.Count != memberIds.Count)
                throw ApiException.BadRequest("Один или несколько пользователей не найдены");

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
                throw ApiException.BadRequest("Администратор магазина должен быть участником этого магазина");

            var admins = store.members.Where(m => adminIds.Contains(m.Id)).ToList();
            store.storeAdmins.Clear();
            foreach (var admin in admins)
                store.storeAdmins.Add(admin);
        }

        await _unitOfWork.SaveChangesAsync();

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpGet]
    [Route("/users/all")]
    public async Task<IActionResult> GetAllUsers()
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || !currentUser.isActive || !currentUser.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут просматривать всех пользователей");

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
    }

    private static IQueryable<Store> StoreIncludes(IQueryable<Store> query) =>
        query.Include(s => s.members).Include(s => s.storeAdmins);

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
