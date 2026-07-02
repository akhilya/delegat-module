using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class InvitationTokenController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    public InvitationTokenController(
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("/tokens/")]
    public async Task<IActionResult> GetInvitationTokens()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null || !user.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут просматривать токены приглашений");

        var tokenRepo = _unitOfWork.Repository<InvitationToken>();
        var tokens = await tokenRepo.FindAllAsync(
            t => true,
            q => q.Include(t => t.usedByUser).OrderByDescending(t => t.createdAt)
        );

        return Ok(tokens.Select(t => t.MapToListDTO(_serviceProvider)));
    }

    [HttpPost]
    [Route("/tokens/")]
    public async Task<IActionResult> PostInvitationToken()
    {
        var user = await _currentUserService.GetUserAsync();
        if (user == null || !user.isAdmin)
            throw ApiException.Forbidden("Только администраторы могут создавать токены приглашений");

        var tokenRepo = _unitOfWork.Repository<InvitationToken>();
        var newToken = new InvitationToken();
        newToken.AddFill(new InvitationToken.AddForm());
        await tokenRepo.AddAsync(newToken);
        await _unitOfWork.SaveChangesAsync();

        return Ok(newToken.MapToDTO(_serviceProvider));
    }
}
