using Delegat.Services.Ai.Agent;
using Delegat.Tests.Infrastructure;

namespace Delegat.Tests;

public class CatalogSearchRankingTests
{
    [Theory]
    [InlineData("SKU-42", "SKU-42", "Similar first")]
    [InlineData("МУГ-42", "МУГ-42", "Похожий товар")]
    public async Task ExactSkuOutranksAlphabeticallyEarlierSubstringWithTightLimit(
        string query, string exactSku, string similarName)
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var user = data.AddUser("ranking");
        var store = data.AddStore("Search store", "RUB", "UTC", user);
        data.AddProduct(store.Id, name: similarName, sku: query + "-ALTERNATIVE");
        var exact = data.AddProduct(store.Id, name: "Z last exact", sku: exactSku);
        await db.SaveChangesAsync();

        var (result, summary) = await new CatalogSearchService(db).SearchAsync(store.Id, query, 1, default);
        var products = (IEnumerable<CatalogSearchHit>)result.GetType().GetProperty("products")!.GetValue(result)!;

        Assert.Equal(exact.Id, Assert.Single(products).Id);
        Assert.True((bool)result.GetType().GetProperty("productsHasMore")!.GetValue(result)!);
        Assert.Contains("at least 2 products", summary, StringComparison.Ordinal);
    }
}
