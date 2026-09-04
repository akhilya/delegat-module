using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Host map of overview tabs and adapter mailboxes onto normalized order statuses.
/// Mirrors cabinet mailbox ids in the SPA; Ozon and Wildberries have no New mailbox.
/// Ozon pack still lists <see cref="MarketplaceOrderStatuses.New"/> and
/// <see cref="MarketplaceOrderStatuses.Unpaid"/> so a rare awaiting_approve row can sync.
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

    public static IReadOnlyList<string> StatusesForOverviewTab(string tab)
    {
        var key = (tab ?? "").Trim();
        if (key.Equals("assembly", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                MarketplaceOrderStatuses.New,
                MarketplaceOrderStatuses.Unpaid,
                MarketplaceOrderStatuses.Confirmed,
                MarketplaceOrderStatuses.AwaitingPack
            ];
        }

        if (key.Equals("ship", StringComparison.OrdinalIgnoreCase))
            return ShipMailbox;
        if (key.Equals("delivery", StringComparison.OrdinalIgnoreCase))
            return DeliveryMailbox;
        if (key.Equals("done", StringComparison.OrdinalIgnoreCase))
            return DoneMailbox;
        return [];
    }

    public static IReadOnlyList<string> ListStatusesForAdapter(string adapterKey)
    {
        var key = adapterKey ?? "";
        if (!MailboxesByAdapter.TryGetValue(key, out var mailboxIds))
            return AllNormalizedExceptUnknown;

        var statuses = new List<string>();
        foreach (var mailboxId in mailboxIds)
        {
            foreach (var status in StatusesForMailbox(mailboxId, key))
            {
                if (!statuses.Contains(status))
                    statuses.Add(status);
            }
        }

        return statuses;
    }

    public static bool IsTerminal(string status) =>
        MarketplaceOrderStatuses.Delivered.Equals(status, StringComparison.OrdinalIgnoreCase)
        || MarketplaceOrderStatuses.Cancelled.Equals(status, StringComparison.OrdinalIgnoreCase)
        || MarketplaceOrderStatuses.Returned.Equals(status, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Statuses listed for one mailbox. Ozon pack appends new/unpaid extras; it is not a New mailbox.
    /// </summary>
    private static IEnumerable<string> StatusesForMailbox(string mailboxId, string adapterKey)
    {
        if (!StatusesByMailbox.TryGetValue(mailboxId, out var mailboxStatuses))
            yield break;

        foreach (var status in mailboxStatuses)
            yield return status;

        if (!mailboxId.Equals("pack", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(adapterKey, "ozon", StringComparison.OrdinalIgnoreCase))
            yield break;

        foreach (var extra in NewMailbox)
            yield return extra;
    }
}
