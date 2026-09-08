using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Host map of overview tabs and adapter mailboxes onto normalized order statuses.
/// Mirrors cabinet mailbox ids in the SPA; Ozon and Wildberries have no New mailbox.
/// Ozon pack still lists <see cref="MarketplaceOrderStatuses.New"/> so a rare
/// awaiting_approve row can sync. Unpaid is not an Ozon list filter.
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

    /// <summary>Ozon has no unpaid list filter; New maps to awaiting_approve.</summary>
    private static readonly string[] OzonPackExtras = [MarketplaceOrderStatuses.New];

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

    private static readonly string[] OverviewTabIds = ["assembly", "ship", "delivery", "done"];

    public static IReadOnlyList<string> MailboxIdsForAdapter(string adapterKey)
    {
        if (MailboxesByAdapter.TryGetValue(adapterKey ?? "", out var ids))
            return ids;
        return ["new", "pack", "ship", "delivery", "done", "closed"];
    }

    public static IReadOnlyDictionary<string, int> CountOverviewTabs(
        IReadOnlyDictionary<string, int>? byStatus)
        => CountGroups(OverviewTabIds, StatusesForOverviewTab, byStatus);

    public static IReadOnlyDictionary<string, int> CountMailboxes(
        string adapterKey,
        IReadOnlyDictionary<string, int>? byStatus)
        => CountGroups(
            MailboxIdsForAdapter(adapterKey),
            id => StatusesForMailbox(id, adapterKey).Distinct().ToArray(),
            byStatus);

    private static Dictionary<string, int> CountGroups(
        IReadOnlyList<string> ids,
        Func<string, IReadOnlyList<string>> statusesOf,
        IReadOnlyDictionary<string, int>? byStatus)
    {
        var src = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (byStatus != null)
        {
            foreach (var kv in byStatus)
                src[kv.Key] = kv.Value;
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var n = 0;
            foreach (var status in statusesOf(id))
            {
                if (src.TryGetValue(status, out var count))
                    n += count;
            }

            result[id] = n;
        }

        return result;
    }

    public static IReadOnlyList<string> StatusesForOverviewTab(string tab)
    {
        var key = (tab ?? "").Trim();
        if (key.Equals("assembly", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ..NewMailbox,
                ..PackMailbox
            ];
        }

        if (StatusesByMailbox.TryGetValue(key, out var mailbox)
            && (key.Equals("ship", StringComparison.OrdinalIgnoreCase)
                || key.Equals("delivery", StringComparison.OrdinalIgnoreCase)
                || key.Equals("done", StringComparison.OrdinalIgnoreCase)))
            return mailbox;
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

    public static readonly string[] TerminalStatusList =
    [
        MarketplaceOrderStatuses.Delivered,
        MarketplaceOrderStatuses.Cancelled,
        MarketplaceOrderStatuses.Returned
    ];

    public static bool IsTerminal(string status)
    {
        foreach (var terminal in TerminalStatusList)
        {
            if (terminal.Equals(status, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Statuses listed for one mailbox. Ozon pack appends New (awaiting_approve); it is not a New mailbox.
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

        foreach (var extra in OzonPackExtras)
            yield return extra;
    }
}
