using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai;

/// <summary>
/// Export results for one mapping since its canvas was last saved.
/// Older adapter errors are omitted: that wiring is no longer the one being fixed.
/// </summary>
public static class MappingExportExperience
{
    public const int MaxGroups = 6;
    public const int MessageMax = 500;

    public static bool IsCurrent(DateTime? eventAt, DateTime? graphUpdatedAt) =>
        eventAt.HasValue && (graphUpdatedAt == null || eventAt.Value >= graphUpdatedAt.Value);

    public static async Task<string?> ForMappingAsync(
        DataContext db,
        int storeId,
        int mappingId,
        string? storeTimezone,
        CancellationToken ct = default)
    {
        var mapping = await db.categoryMappings.AsNoTracking()
            .Where(m => m.Id == mappingId && m.connection.storeId == storeId)
            .Select(m => new { m.connectionId, m.localCategoryId, m.graphUpdatedAt })
            .FirstOrDefaultAsync(ct);
        if (mapping == null)
            return null;

        var since = mapping.graphUpdatedAt;
        var onlyThis = await db.categoryMappings.AsNoTracking()
            .CountAsync(m => m.connectionId == mapping.connectionId && m.localCategoryId == mapping.localCategoryId, ct) <= 1;

        var links = db.productMarketplaceLinks.AsNoTracking()
            .Where(l => l.connectionId == mapping.connectionId
                && l.product.categoryId == mapping.localCategoryId
                && l.product.storeId == storeId
                && (l.categoryMappingId == mappingId || (onlyThis && l.categoryMappingId == null))
                && l.lastSyncAt != null
                && (since == null || l.lastSyncAt >= since));

        var synced = await links.CountAsync(
            l => l.status == ProductMarketplaceStatus.Synced || l.status == ProductMarketplaceStatus.Modified, ct);

        var linkFailures = await links
            .Where(l => l.status == ProductMarketplaceStatus.SyncError || l.status == ProductMarketplaceStatus.RemoteMissing)
            .OrderByDescending(l => l.lastSyncAt)
            .Select(l => new Outcome(l.productId, l.product.name, l.lastErrorMessage, l.lastSyncAt))
            .Take(40)
            .ToListAsync(ct);

        var pending = await (
            from e in db.pendingProductExports.AsNoTracking()
            join p in db.products.AsNoTracking() on e.productId equals p.Id
            where e.storeId == storeId
                && e.connectionId == mapping.connectionId
                && e.status == ProductExportStatus.Failed
                && p.categoryId == mapping.localCategoryId
                && (since == null || (e.lastAttemptAt ?? e.createdAt) >= since)
            orderby e.lastAttemptAt descending
            select new Outcome(e.productId, e.productName ?? p.name, e.lastError, e.lastAttemptAt ?? e.createdAt)
        ).Take(40).ToListAsync(ct);

        var linkedIds = linkFailures.Select(f => f.ProductId).ToHashSet();
        var currentIds = await links.Select(l => l.productId).ToListAsync(ct);
        linkedIds.UnionWith(currentIds);

        var outcomes = linkFailures
            .Concat(pending.Where(p => !linkedIds.Contains(p.ProductId)))
            .ToList();

        var groups = outcomes
            .GroupBy(o => Normalize(o.Message))
            .Select(g => new FailureGroup(g.Count(), g.Select(x => x.ProductName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? $"#{g.First().ProductId}", g.First().Message ?? ""))
            .OrderByDescending(g => g.Count)
            .Take(MaxGroups)
            .ToList();

        var savedAt = since.HasValue
            ? StoreTimeHelper.FormatInStoreTimezone(since.Value, storeTimezone ?? StoreTimeHelper.DefaultTimezone)
            : null;
        return Format(since, savedAt, synced, outcomes.Count, groups);
    }

    public static string Format(
        DateTime? graphUpdatedAt,
        string? savedAtLabel,
        int synced,
        int failed,
        IReadOnlyList<FailureGroup> failures)
    {
        if (graphUpdatedAt.HasValue && synced == 0 && failed == 0)
        {
            return "=== MAPPING EXPERIENCE ===\n" +
                   $"Canvas last saved {savedAtLabel}. No exports since that save. " +
                   "Earlier successes and failures are not open problems. Do not solve them again.";
        }

        if (!graphUpdatedAt.HasValue && synced == 0 && failed == 0)
        {
            return "=== MAPPING EXPERIENCE ===\n" +
                   "No export results for this mapping yet.";
        }

        var when = graphUpdatedAt.HasValue
            ? $"Since the canvas was saved {savedAtLabel}. Earlier results are not open problems."
            : "Latest export results for this mapping. After the canvas is saved, only exports that run after that save stay relevant.";

        var lines = new List<string>
        {
            "=== MAPPING EXPERIENCE ===",
            when,
            $"Synced: {synced}. Failed: {failed}.",
        };

        if (failures.Count > 0)
        {
            lines.Add("Still failing (what the adapter returned):");
            foreach (var failure in failures)
            {
                var message = failure.AdapterMessage.Trim();
                if (message.Length > MessageMax)
                    message = message[..MessageMax];
                var name = failure.ExampleProduct.Replace("\r", " ").Replace("\n", " ");
                lines.Add($"- {failure.Count} product(s), e.g. \"{name}\": {message}");
            }
        }

        lines.Add("Fix only a failure listed here. A failure that is not listed was already left behind by a later canvas save.");
        return string.Join('\n', lines);
    }

    private static string Normalize(string? message)
    {
        var text = (message ?? "").Trim();
        if (text.Length > MessageMax)
            text = text[..MessageMax];
        return text;
    }

    private sealed record Outcome(int ProductId, string? ProductName, string? Message, DateTime? At);

    public sealed record FailureGroup(int Count, string ExampleProduct, string AdapterMessage);
}
