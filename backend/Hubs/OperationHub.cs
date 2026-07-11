using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Delegat.Hubs;

[Authorize]
public class OperationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var storeIdCookie = Context.GetHttpContext()?.Request.Cookies["selected_store_id"];
        if (storeIdCookie != null && int.TryParse(storeIdCookie, out var storeId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"store-{storeId}");

        await base.OnConnectedAsync();
    }
}
