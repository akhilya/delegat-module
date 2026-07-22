using System.Text.Json;
using Delegat.Marketplace.Wildberries.Models;

namespace Delegat.Tests;

public class WildberriesCardDeserializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    [Fact]
    public void CardsList_DeserializesNullAndLargeImtId()
    {
        const string json = """
            {
              "cards": [
                { "nmID": 1, "imtID": null, "vendorCode": "a", "title": "A" },
                { "nmID": 2, "imtID": 3147483647, "vendorCode": "b", "title": "B" },
                { "nmID": 3, "imtID": "42", "vendorCode": "c", "title": "C" }
              ],
              "cursor": { "nmID": 3, "total": 3 }
            }
            """;

        var response = JsonSerializer.Deserialize<WbCardsListResponse>(json, Options);

        Assert.NotNull(response?.Cards);
        Assert.Equal(3, response.Cards.Count);
        Assert.Null(response.Cards[0].ImtId);
        Assert.Equal(3147483647L, response.Cards[1].ImtId);
        Assert.Equal(42L, response.Cards[2].ImtId);
    }
}
