using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Services.Marketplace;

public sealed class StoredOrderListItem
{
    public int ConnectionId { get; init; }
    public string AdapterKey { get; init; } = "";
    public string ConnectionName { get; init; } = "";
    public ExternalOrder Order { get; init; } = null!;
}
