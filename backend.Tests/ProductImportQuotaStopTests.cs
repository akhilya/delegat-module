using Delegat.Models;
using Delegat.Services;

namespace Delegat.Tests;

public class ProductImportQuotaStopTests
{
    [Fact]
    public void MarkRemainingFailed_SkipsCurrentCompletedAndCancelled()
    {
        var current = Item(1, ProductImportStatus.Failed);
        var pending = Item(2, ProductImportStatus.Pending);
        var inProgress = Item(3, ProductImportStatus.InProgress);
        var retryable = Item(4, ProductImportStatus.Failed, retryCount: 1);
        var done = Item(5, ProductImportStatus.Completed);
        var cancelled = Item(6, ProductImportStatus.Cancelled);

        var skipped = ProductImportQuotaStop.MarkRemainingFailed(
            [current, pending, inProgress, retryable, done, cancelled],
            excludeId: 1,
            error: "Product limit reached (MAX_PRODUCTS=10)");

        Assert.Equal(3, skipped);
        Assert.Equal(ProductImportStatus.Failed, pending.status);
        Assert.Equal(PendingProductImport.MAX_RETRIES, pending.retryCount);
        Assert.Equal("Product limit reached (MAX_PRODUCTS=10)", pending.lastError);
        Assert.Equal(ProductImportStatus.Failed, inProgress.status);
        Assert.Equal(PendingProductImport.MAX_RETRIES, retryable.retryCount);
        Assert.Equal(ProductImportStatus.Completed, done.status);
        Assert.Equal(ProductImportStatus.Cancelled, cancelled.status);
        Assert.Equal(0, current.retryCount);
    }

    [Fact]
    public void RemainingForQuota_SameBatchOnly()
    {
        var hit = Item(1, ProductImportStatus.Failed, batchId: "a");
        var same = Item(2, ProductImportStatus.Pending, batchId: "a");
        var other = Item(3, ProductImportStatus.Pending, batchId: "b");

        var remaining = ProductImportQuotaStop.RemainingForQuota([hit, same, other], hit).ToList();

        Assert.Single(remaining);
        Assert.Equal(2, remaining[0].Id);
    }

    [Fact]
    public void RemainingForQuota_NoBatch_SameStore()
    {
        var hit = Item(1, ProductImportStatus.Failed, batchId: null, storeId: 9);
        var sameStore = Item(2, ProductImportStatus.Pending, batchId: null, storeId: 9);
        var otherStore = Item(3, ProductImportStatus.Pending, batchId: null, storeId: 8);

        var remaining = ProductImportQuotaStop.RemainingForQuota(
            [hit, sameStore, otherStore], hit).ToList();

        Assert.Single(remaining);
        Assert.Equal(2, remaining[0].Id);
    }

    [Fact]
    public void RemainingForQuota_SkipsClosedRows()
    {
        var hit = Item(1, ProductImportStatus.Failed, batchId: "a");
        var pending = Item(2, ProductImportStatus.Pending, batchId: "a");
        var done = Item(3, ProductImportStatus.Completed, batchId: "a");
        var exhausted = Item(4, ProductImportStatus.Failed, retryCount: PendingProductImport.MAX_RETRIES, batchId: "a");
        var otherBatch = Item(5, ProductImportStatus.Pending, batchId: "b");

        var remaining = ProductImportQuotaStop.RemainingForQuota(
            [hit, pending, done, exhausted, otherBatch], hit).ToList();

        Assert.Single(remaining);
        Assert.Equal(2, remaining[0].Id);
    }

    private static PendingProductImport Item(
        int id,
        ProductImportStatus status,
        int retryCount = 0,
        string? batchId = "batch",
        int storeId = 1)
        => new()
        {
            Id = id,
            status = status,
            retryCount = retryCount,
            batchId = batchId,
            storeId = storeId,
        };
}
