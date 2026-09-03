using Delegat.Marketplace.SDK.DTOs;
using HostPage = Delegat.Models.Marketplace.DTOs.PagedResult<Delegat.Marketplace.SDK.DTOs.ExternalPromotion>;

namespace Delegat.Services.Tools;

public sealed class PromotionClearSummary
{
    public int Promotions { get; init; }
    public int Removed { get; init; }
    public int Failed { get; init; }
    public int Skipped { get; init; }
}

/// <summary>
/// Pages marketplace promotions and unenrolls every product (adapter <c>RemoveAll</c>).
/// </summary>
public static class PromotionMembershipClearer
{
    public const int MaxPages = 50;

    public static async Task<PromotionClearSummary> ClearAsync(
        Func<PromotionQuery, CancellationToken, Task<HostPage>> list,
        Func<string, PromotionProductsUpdate, CancellationToken, Task<PromotionProductsResult>> update,
        Func<string, string, Task>? log = null,
        CancellationToken ct = default)
    {
        var promotions = 0;
        var removed = 0;
        var failed = 0;
        var skipped = 0;
        var page = 1;
        string? cursor = null;

        for (var i = 0; i < MaxPages; i++)
        {
            var query = new PromotionQuery
            {
                Page = page,
                PageSize = 100,
                Cursor = cursor
            };
            var result = await list(query, ct);
            var items = result.Items ?? [];
            if (items.Count == 0)
                break;

            foreach (var promo in items)
            {
                if (string.IsNullOrWhiteSpace(promo.ExternalId))
                    continue;
                promotions++;
                var change = await update(
                    promo.ExternalId,
                    new PromotionProductsUpdate { RemoveAll = true },
                    ct);
                if (change.Success)
                {
                    removed += Math.Max(change.Removed, 0);
                    if (log != null)
                    {
                        await log(
                            "info",
                            $"Cleared promotion {promo.ExternalId} ({promo.Name}): removed {change.Removed}");
                    }
                    continue;
                }

                var message = change.ErrorMessage ?? "Failed";
                if (IsRemovalUnsupported(message))
                {
                    skipped++;
                    if (log != null)
                        await log("warning", $"{promo.ExternalId}: {message}");
                }
                else
                {
                    failed++;
                    if (log != null)
                        await log("error", $"{promo.ExternalId}: {message}");
                }
            }

            if (!result.HasMore)
                break;
            page++;
            cursor = result.NextCursor;
        }

        return new PromotionClearSummary
        {
            Promotions = promotions,
            Removed = removed,
            Failed = failed,
            Skipped = skipped
        };
    }

    public static bool IsRemovalUnsupported(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        return message.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            || message.Contains("only adds", StringComparison.OrdinalIgnoreCase);
    }
}
