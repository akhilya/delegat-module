using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Seller actions move an order to the next cabinet mailbox immediately.
/// A marketplace read-back often still shows the previous status for a while
/// (Ozon assemble is the usual case). The snapshot uses the action's status,
/// and a stale list cannot put the order back during <see cref="StaleListHold"/>.
/// </summary>
public static class MarketplaceSellerOrderStatus
{
    public static readonly TimeSpan StaleListHold = TimeSpan.FromMinutes(2);

    public static void Advance(string adapterKey, string action, ExternalOrder order)
    {
        var next = NextStatus(adapterKey, action);
        if (next == null)
            return;
        if (!IsTerminal(next) && !AdapterListsStatus(adapterKey, next))
            return;
        if (IsTerminal(next) || Rank(order.Status) < Rank(next))
            order.Status = next;
    }

    public static bool ShouldKeepStoredStatus(
        string storedStatus,
        DateTime storedSyncedAt,
        string? incomingStatus,
        DateTime now)
    {
        if (storedSyncedAt == default)
            return false;
        if (now - storedSyncedAt >= StaleListHold)
            return false;
        if (IsTerminal(incomingStatus))
            return false;
        return Rank(incomingStatus) < Rank(storedStatus);
    }

    private static string? NextStatus(string adapterKey, string action)
    {
        var key = (adapterKey ?? "").Trim().ToLowerInvariant();
        var act = (action ?? "").Trim().ToLowerInvariant();
        if (act.Length == 0)
            return null;

        if (act == MarketplaceOrderActions.Cancel)
            return MarketplaceOrderStatuses.Cancelled;

        if (key == "ozon")
        {
            return act is MarketplaceOrderActions.Pack or MarketplaceOrderActions.Confirm
                ? MarketplaceOrderStatuses.AwaitingDeliver
                : null;
        }

        if (key == "wildberries")
        {
            return act switch
            {
                MarketplaceOrderActions.Pack or MarketplaceOrderActions.Confirm
                    => MarketplaceOrderStatuses.AwaitingDeliver,
                MarketplaceOrderActions.Ship => MarketplaceOrderStatuses.Delivering,
                _ => null
            };
        }

        if (key == "yandex")
        {
            return act switch
            {
                MarketplaceOrderActions.Confirm => MarketplaceOrderStatuses.Confirmed,
                MarketplaceOrderActions.Ship => MarketplaceOrderStatuses.Delivering,
                MarketplaceOrderActions.Deliver or MarketplaceOrderActions.Complete
                    => MarketplaceOrderStatuses.Delivered,
                _ => null
            };
        }

        return act switch
        {
            MarketplaceOrderActions.Confirm => MarketplaceOrderStatuses.Confirmed,
            MarketplaceOrderActions.Pack => MarketplaceOrderStatuses.AwaitingDeliver,
            MarketplaceOrderActions.Ship => MarketplaceOrderStatuses.Delivering,
            MarketplaceOrderActions.Deliver or MarketplaceOrderActions.Complete
                => MarketplaceOrderStatuses.Delivered,
            _ => null
        };
    }

    private static bool AdapterListsStatus(string adapterKey, string status)
    {
        foreach (var listed in MarketplaceOrderBuckets.ListStatusesForAdapter(adapterKey))
        {
            if (listed.Equals(status, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsTerminal(string? status)
    {
        var value = (status ?? "").Trim().ToLowerInvariant();
        return value is MarketplaceOrderStatuses.Cancelled
            or MarketplaceOrderStatuses.Returned;
    }

    private static int Rank(string? status)
        => (status ?? "").Trim().ToLowerInvariant() switch
        {
            MarketplaceOrderStatuses.New or MarketplaceOrderStatuses.Unpaid => 10,
            MarketplaceOrderStatuses.Confirmed => 20,
            MarketplaceOrderStatuses.AwaitingPack => 30,
            MarketplaceOrderStatuses.AwaitingDeliver => 40,
            MarketplaceOrderStatuses.Delivering => 50,
            MarketplaceOrderStatuses.Delivered => 60,
            MarketplaceOrderStatuses.Returned => 70,
            MarketplaceOrderStatuses.Cancelled => 80,
            _ => 0
        };
}
