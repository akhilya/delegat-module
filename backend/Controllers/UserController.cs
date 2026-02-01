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
public class UserController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    public UserController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("/users/me/memberships")]
    public async Task<IActionResult> GetMyMemberships()
    {
        var userId = _currentUserService.GetUserId();
        if (userId == null)
            throw ApiException.Unauthorized();

        var storeRepo = _unitOfWork.Repository<Store>();
        var stores = await storeRepo.FindAllAsync(
            s => s.members.Any(m => m.Id == userId.Value),
            query => query.Include(s => s.members));

        var result = stores.Select(s => s.MapToDTO(_serviceProvider)).ToList();
        return Ok(result);
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
            throw ApiException.NotFound("Store not found");

        if (!store.members.Any(m => m.Id == user.Id))
            throw ApiException.Forbidden("You are not a member of this store");

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
}
