using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Hubs;

[Authorize]
public class OperationHub : Hub
{
    private readonly DataContext _context;

    public OperationHub(DataContext context)
    {
        _context = context;
    }

    public override async Task OnConnectedAsync()
    {
        var storeId = await ResolveMemberStoreIdAsync(Context.ConnectionAborted);
        if (storeId != null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"store-{storeId.Value}", Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }

    private async Task<int?> ResolveMemberStoreIdAsync(CancellationToken ct)
    {
        var storeIdCookie = Context.GetHttpContext()?.Request.Cookies["selected_store_id"];
        if (storeIdCookie == null || !int.TryParse(storeIdCookie, out var storeId))
            return null;

        var userIdClaim = Context.User?.FindFirst("userId") ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return null;

        var isMember = await _context.stores
            .AnyAsync(s => s.Id == storeId && s.members.Any(m => m.Id == userId && m.isActive), ct);

        return isMember ? storeId : null;
    }
}
