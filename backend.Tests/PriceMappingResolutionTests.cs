using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class PriceMappingResolutionTests
{
    [Fact]
    public void UsesTheOnlyCategoryMappingWhenTheLinkHasNone()
    {
        var only = new CategoryMapping { Id = 7, localCategoryId = 1 };
        var resolved = PriceSyncService.ResolvePriceMapping(
            savedMappingId: null,
            productCategoryId: 1,
            new Dictionary<int, CategoryMapping> { [7] = only },
            new Dictionary<int, List<CategoryMapping>> { [1] = [only] });

        Assert.Equal(7, resolved?.Id);
    }

    [Fact]
    public void KeepsASavedMappingThatStillMatchesTheCategory()
    {
        var saved = new CategoryMapping { Id = 3, localCategoryId = 1 };
        var other = new CategoryMapping { Id = 8, localCategoryId = 1 };
        var resolved = PriceSyncService.ResolvePriceMapping(
            savedMappingId: 3,
            productCategoryId: 1,
            new Dictionary<int, CategoryMapping> { [3] = saved, [8] = other },
            new Dictionary<int, List<CategoryMapping>> { [1] = [saved, other] });

        Assert.Equal(3, resolved?.Id);
    }

    [Fact]
    public void DoesNotGuessWhenSeveralMappingsAndNoneIsSelected()
    {
        var a = new CategoryMapping { Id = 1, localCategoryId = 4 };
        var b = new CategoryMapping { Id = 2, localCategoryId = 4 };
        var resolved = PriceSyncService.ResolvePriceMapping(
            savedMappingId: null,
            productCategoryId: 4,
            new Dictionary<int, CategoryMapping> { [1] = a, [2] = b },
            new Dictionary<int, List<CategoryMapping>> { [4] = [a, b] });

        Assert.Null(resolved);
    }
}
