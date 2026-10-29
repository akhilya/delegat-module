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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IServiceProvider _serviceProvider;

    public InvitationTokenController(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
    }

    [HttpGet]
    [Route("/tokens/")]
    [RequiresPermission(Permissions.UsersManage)]
    public async Task<IActionResult> GetInvitationTokens()
    {
        var tokenRepo = _unitOfWork.Repository<InvitationToken>();
        var tokens = await tokenRepo.FindAllAsync(
            t => true,
            q => q.Include(t => t.usedByUser).OrderByDescending(t => t.createdAt)
        );

        return Ok(tokens.Select(t => t.MapToListDTO(_serviceProvider)));
    }

    [HttpPost]
    [Route("/tokens/")]
    [RequiresPermission(Permissions.UsersManage)]
    public async Task<IActionResult> PostInvitationToken()
    {
        var tokenRepo = _unitOfWork.Repository<InvitationToken>();
        var newToken = new InvitationToken();
        newToken.AddFill(new InvitationToken.AddForm());
        await tokenRepo.AddAsync(newToken);
        await _unitOfWork.SaveChangesAsync();

        return Ok(newToken.MapToDTO(_serviceProvider));
    }

    /// <summary>Revokes an unused invitation token by deleting it.</summary>
    [HttpDelete]
    [Route("/tokens/{tokenId:int}")]
    [RequiresPermission(Permissions.UsersManage)]
    public async Task<IActionResult> DeleteInvitationToken(int tokenId)
    {
        var tokenRepo = _unitOfWork.Repository<InvitationToken>();
        var token = await tokenRepo.FindAsync(t => t.Id == tokenId);
        if (token == null)
            throw ApiException.NotFound(code: "token_not_found");

        if (token.used)
            throw ApiException.BadRequest(code: "token_already_used");

        tokenRepo.DeleteWithoutSave(token);
        await _unitOfWork.SaveChangesAsync();

        return Ok(new { success = true });
    }
}
