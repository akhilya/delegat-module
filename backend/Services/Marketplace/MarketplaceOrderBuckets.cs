using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Host map of overview tabs and adapter mailboxes onto normalized order statuses.
/// Mirrors cabinet mailbox ids in the SPA; Ozon/WB have no New mailbox.
/// </summary>
public static class MarketplaceOrderBuckets
{
    private static readonly string[] NewMailbox =
    [
        MarketplaceOrderStatuses.New,
        MarketplaceOrderStatuses.Unpaid
    ];

    private static readonly string[] PackMailbox =
    [
        MarketplaceOrderStatuses.Confirmed,
        MarketplaceOrderStatuses.AwaitingPack
    ];

    private static readonly string[] ShipMailbox = [MarketplaceOrderStatuses.AwaitingDeliver];
    private static readonly string[] DeliveryMailbox = [MarketplaceOrderStatuses.Delivering];
    private static readonly string[] DoneMailbox = [MarketplaceOrderStatuses.Delivered];

    private static readonly string[] ClosedMailbox =
    [
        MarketplaceOrderStatuses.Cancelled,
        MarketplaceOrderStatuses.Returned
    ];

    private static readonly Dictionary<string, string[]> StatusesByMailbox =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["new"] = NewMailbox,
            ["pack"] = PackMailbox,
            ["ship"] = ShipMailbox,
            ["delivery"] = DeliveryMailbox,
            ["done"] = DoneMailbox,
            ["closed"] = ClosedMailbox,
        };

    private static readonly Dictionary<string, string[]> MailboxesByAdapter =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ozon"] = ["pack", "ship", "delivery", "done", "closed"],
            ["wildberries"] = ["pack", "ship", "delivery", "done", "closed"],
            ["yandex"] = ["new", "pack", "ship", "delivery", "done", "closed"],
            ["woocommerce"] = ["new", "pack", "done", "closed"],
            ["opencart"] = ["new", "pack", "done", "closed"],
        };

    private static readonly string[] AllNormalizedExceptUnknown =
    [
        MarketplaceOrderStatuses.New,
        MarketplaceOrderStatuses.Unpaid,
        MarketplaceOrderStatuses.Confirmed,
        MarketplaceOrderStatuses.AwaitingPack,
        MarketplaceOrderStatuses.AwaitingDeliver,
        MarketplaceOrderStatuses.Delivering,
        MarketplaceOrderStatuses.Delivered,
        MarketplaceOrderStatuses.Cancelled,
        MarketplaceOrderStatuses.Returned
    ];

    public static IReadOnlyList<string> StatusesForOverviewTab(string tab) => tab switch
    {
        "assembly" =>
        [
            MarketplaceOrderStatuses.New,
            MarketplaceOrderStatuses.Unpaid,
            MarketplaceOrderStatuses.Confirmed,
            MarketplaceOrderStatuses.AwaitingPack
        ],
        "ship" => ShipMailbox,
        "delivery" => DeliveryMailbox,
        "done" => DoneMailbox,
        _ => []
    };

    public static IReadOnlyList<string> ListStatusesForAdapter(string adapterKey)
    {
        if (!MailboxesByAdapter.TryGetValue(adapterKey ?? "", out var mailboxIds))
            return AllNormalizedExceptUnknown;

        var statuses = new List<string>();
        foreach (var mailboxId in mailboxIds)
        {
            if (!StatusesByMailbox.TryGetValue(mailboxId, out var mailboxStatuses))
                continue;
            foreach (var status in mailboxStatuses)
            {
                if (!statuses.Contains(status))
                    statuses.Add(status);
            }
        }

        return statuses;
    }

    public static bool IsTerminal(string status) =>
        status is MarketplaceOrderStatuses.Delivered
            or MarketplaceOrderStatuses.Cancelled
            or MarketplaceOrderStatuses.Returned;
}
