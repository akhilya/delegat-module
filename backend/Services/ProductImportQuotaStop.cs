using Delegat.Models;

namespace Delegat.Services;

/// <summary>
/// When MAX_PRODUCTS is hit mid-batch, fail the rest of the queue in one pass
/// instead of importing each leftover row (and retrying).
/// </summary>
internal static class ProductImportQuotaStop
{
    public static IEnumerable<PendingProductImport> RemainingForQuota(
        IEnumerable<PendingProductImport> items,
        PendingProductImport hit)
    {
        foreach (var p in items)
        {
            if (p.Id == hit.Id)
                continue;
            if (!string.IsNullOrEmpty(hit.batchId))
            {
                if (p.batchId != hit.batchId)
                    continue;
            }
            else if (p.storeId != hit.storeId || !string.IsNullOrEmpty(p.batchId))
            {
                continue;
            }

            if (!IsOpen(p))
                continue;

            yield return p;
        }
    }

    public static int MarkRemainingFailed(
        IEnumerable<PendingProductImport> items,
        int excludeId,
        string error)
    {
        var n = 0;
        foreach (var p in items)
        {
            if (p.Id == excludeId)
                continue;
            if (p.status is ProductImportStatus.Completed or ProductImportStatus.Cancelled)
                continue;

            p.status = ProductImportStatus.Failed;
            p.lastError = error;
            p.retryCount = PendingProductImport.MAX_RETRIES;
            n++;
        }

        return n;
    }

    private static bool IsOpen(PendingProductImport p) =>
        p.status == ProductImportStatus.Pending
        || p.status == ProductImportStatus.InProgress
        || (p.status == ProductImportStatus.Failed && p.retryCount < PendingProductImport.MAX_RETRIES);
}
