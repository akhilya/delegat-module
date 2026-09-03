using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Tools;
using HostPage = Delegat.Models.Marketplace.DTOs.PagedResult<Delegat.Marketplace.SDK.DTOs.ExternalPromotion>;

namespace Delegat.Tests;

public class PromotionMembershipClearerTests
{
    [Fact]
    public async Task ClearAsync_CallsRemoveAllOnEachPromotion()
    {
        var updates = new List<string>();
        var summary = await PromotionMembershipClearer.ClearAsync(
            (_, _) => Task.FromResult(new HostPage(
            [
                new ExternalPromotion { ExternalId = "a", Name = "A" },
                new ExternalPromotion { ExternalId = "b", Name = "B" },
            ], false)),
            (id, update, _) =>
            {
                Assert.True(update.RemoveAll);
                updates.Add(id);
                return Task.FromResult(new PromotionProductsResult { Success = true, Removed = 3 });
            });

        Assert.Equal(["a", "b"], updates);
        Assert.Equal(2, summary.Promotions);
        Assert.Equal(6, summary.Removed);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Skipped);
    }

    [Fact]
    public async Task ClearAsync_SkipsUnsupportedRemoval()
    {
        var summary = await PromotionMembershipClearer.ClearAsync(
            (_, _) => Task.FromResult(new HostPage(
            [
                new ExternalPromotion { ExternalId = "wb-1", Name = "Calendar" },
            ], false)),
            (_, _, _) => Task.FromResult(new PromotionProductsResult
            {
                Success = false,
                ErrorMessage = "Wildberries calendar promotions API only adds nomenclatures; removal is not supported"
            }));

        Assert.Equal(1, summary.Promotions);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Failed);
    }

    [Fact]
    public async Task ClearAsync_CountsHardFailures()
    {
        var summary = await PromotionMembershipClearer.ClearAsync(
            (_, _) => Task.FromResult(new HostPage(
            [
                new ExternalPromotion { ExternalId = "p1" },
            ], false)),
            (_, _, _) => Task.FromResult(new PromotionProductsResult
            {
                Success = false,
                ErrorMessage = "Action is closed"
            }));

        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, summary.Skipped);
    }
}
