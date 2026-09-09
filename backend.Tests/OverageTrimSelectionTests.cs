using Delegat.Models.Products;
using Delegat.Services;

namespace Delegat.Tests;

public class OverageTrimSelectionTests
{
    [Fact]
    public void SelectProductEntities_UnderLimit_Empty()
    {
        var products = new[]
        {
            P(1, 1),
            P(2, 2),
        };

        var selected = OverageTrimService.SelectProductEntities(products, 5);
        Assert.Empty(selected);
    }

    [Fact]
    public void SelectProductEntities_PicksOldestActive_AndAllTrash()
    {
        var products = new[]
        {
            P(1, 1),
            P(2, 10),
            P(3, 5),
            P(4, 3, true),
            P(5, 20, true),
        };

        var selected = OverageTrimService.SelectProductEntities(products, 1);
        Assert.Equal(4, selected.Count);
        Assert.True(selected.Any(p => p.Id == 4));
        Assert.True(selected.Any(p => p.Id == 5));
        Assert.True(selected.Any(p => p.Id == 2));
        Assert.True(selected.Any(p => p.Id == 3));
        Assert.False(selected.Any(p => p.Id == 1));
    }

    [Fact]
    public void SelectProductEntities_NoLimit_Empty()
    {
        var products = new[] { P(1), P(2), P(3) };
        Assert.Empty(OverageTrimService.SelectProductEntities(products, null));
    }

    [Fact]
    public void SelectProductEntities_TrashCountsTowardCap()
    {
        var products = new[]
        {
            P(1, 1),
            P(2, 2),
            P(3, 10, true),
            P(4, 3, true),
        };

        var selected = OverageTrimService.SelectProductEntities(products, 3);
        Assert.Equal(3, selected.Single().Id);
    }

    private static Product P(int id, int daysAgo = 0, bool trashed = false)
    {
        return new Product
        {
            Id = id,
            storeId = 1,
            name = "P" + id,
            sku = "S" + id,
            description = "",
            updatedAt = DateTime.UtcNow.AddDays(-daysAgo),
            createdAt = DateTime.UtcNow.AddDays(-daysAgo),
            deletedAt = trashed ? DateTime.UtcNow.AddDays(-daysAgo) : null,
        };
    }
}
