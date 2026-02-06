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
        var store = await storeRepo.GetByIdWithIncludesAsync(id, query => query.Include(s => s.members));

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        return Ok(store.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/stores/")]
    [AllowAnonymous]
    public async Task<IActionResult> PostStore([FromBody] AddForm input)
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null || !user.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут создавать магазины");

        var storeRepo = _unitOfWork.Repository<Store>();
        var existingStore = await storeRepo.FindAsync(s => s.name == input.name);

        if (existingStore != null)
            throw ApiException.BadRequest("Магазин с таким именем уже существует");

        var newStore = new Store();
        newStore.AddFill(input);
        await storeRepo.AddAsync(newStore);

        // Add creator as member
        newStore.UpdateFill(new UserJoinForm(user));
        await storeRepo.UpdateAsync(newStore);
        await _unitOfWork.SaveChangesAsync();

        return Ok(newStore.MapToDTO(_serviceProvider));
    }

    [HttpPost]
    [Route("/stores/{id}/members/")]
    public async Task<IActionResult> PostAddStoreMember(int id, [FromBody] AddMemberForm input)
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут добавлять участников");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, query => query.Include(s => s.members));

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        if (store.members.Any(m => m.Id == input.userId))
            throw ApiException.BadRequest("Пользователь уже является участником");

        var userRepo = _unitOfWork.Repository<User>();
        var inviteUser = await userRepo.GetByIdTrackingAsync(input.userId);

        if (inviteUser == null)
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
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут удалять участников");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesTrackingAsync(id, query => query.Include(s => s.members));

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        var memberToRemove = store.members.FirstOrDefault(m => m.Id == userId);
        if (memberToRemove == null)
            throw ApiException.BadRequest("Пользователь не является участником этого магазина");

        store.members.Remove(memberToRemove);

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
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут просматривать детали магазина");

        var storeRepo = _unitOfWork.Repository<Store>();
        var store = await storeRepo.GetByIdWithIncludesAsync(id, query => query.Include(s => s.members));

        if (store == null)
            throw ApiException.NotFound("Магазин не найден");

        return Ok(store.MapToDetailDTO(_serviceProvider));
    }

    [HttpGet]
    [Route("/users/all")]
    public async Task<IActionResult> GetAllUsers()
    {
        var currentUser = await _currentUserService.GetUserAsync();
        if (currentUser == null || !currentUser.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут просматривать всех пользователей");

        var userRepo = _unitOfWork.Repository<User>();
        var users = await userRepo.GetAllAsync();

        return Ok(users.Select(u => new { id = u.Id, userName = u.UserName }).ToList());
    }

    public struct AddMemberForm
    {
        public int userId { get; set; }
    }
}
