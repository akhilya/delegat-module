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
                { "nmID": 3, "imtID": "42", "vendorCode": "c", "title": "C" },
                { "nmID": 4, "imtID": 12.0, "vendorCode": "d", "title": "D" },
                { "nmID": 5, "imtID": "", "vendorCode": "e", "title": "E" }
              ],
              "cursor": { "nmID": 5, "total": 5 }
            }
            """;

        var response = JsonSerializer.Deserialize<WbCardsListResponse>(json, Options);

        Assert.NotNull(response?.Cards);
        Assert.Equal(5, response.Cards.Count);
        Assert.Null(response.Cards[0].ImtId);
        Assert.Equal(3147483647L, response.Cards[1].ImtId);
        Assert.Equal(42L, response.Cards[2].ImtId);
        Assert.Equal(12L, response.Cards[3].ImtId);
        Assert.Null(response.Cards[4].ImtId);
    }

    [Fact]
    public void CardsList_DeserializesNmIdOutsideInt32AndAsString()
    {
        const string json = """
            {
              "cards": [
                { "nmID": 3147483647, "vendorCode": "a", "title": "A" },
                { "nmID": "42", "vendorCode": "b", "title": "B" },
                { "nmID": 12.0, "vendorCode": "c", "title": "C" }
              ],
              "cursor": { "nmID": "3147483647", "total": 3 }
            }
            """;

        var response = JsonSerializer.Deserialize<WbCardsListResponse>(json, Options);

        Assert.NotNull(response?.Cards);
        Assert.Equal(3, response.Cards.Count);
        Assert.Equal(3147483647L, response.Cards[0].NmId);
        Assert.Equal(42L, response.Cards[1].NmId);
        Assert.Equal(12L, response.Cards[2].NmId);
        Assert.Equal(3147483647L, response.Cursor!.NmId);
    }
}
