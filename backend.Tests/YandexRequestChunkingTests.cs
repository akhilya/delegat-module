using System.Text;
using System.Text.Json;
using Delegat.Marketplace.YandexMarket;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Tests;

public class YandexRequestChunkingTests
{
    private static UpdateOfferMappingDto MakeOffer(string id, int descriptionChars)
        => new()
        {
            Offer = new UpdateOfferDto
            {
                OfferId = id,
                Name = $"Product {id}",
                Description = new string('x', descriptionChars),
                Pictures = Enumerable.Range(0, 5)
                    .Select(i => $"https://cdn.example.com/images/{id}/{i}.jpg?w=1200&h=1200")
                    .ToList()
            }
        };

    [Fact]
    public void ChunkByJsonSize_RespectsMaxBodyBytes()
    {
        // ~50 rich offers historically produced ~646 KB payloads vs Yandex ~500 KB limit.
        var items = Enumerable.Range(1, 50)
            .Select(i => MakeOffer($"SKU-{i}", descriptionChars: 12_000))
            .ToList();

        var chunks = YandexRequestChunking.ChunkByJsonSize(
            items,
            arrayPropertyName: "offerMappings");

        Assert.True(chunks.Count > 1, "Expected size-based splitting for large payloads");
        Assert.Equal(50, chunks.Sum(c => c.Count));

        foreach (var chunk in chunks)
        {
            var body = new UpdateOfferMappingsRequest { OfferMappings = chunk };
            var bytes = Encoding.UTF8.GetByteCount(
                JsonSerializer.Serialize(body, YandexRequestChunking.JsonOptions));
            Assert.True(
                bytes <= YandexRequestChunking.MaxRequestBodyBytes,
                $"Chunk of {chunk.Count} items is {bytes} bytes, limit {YandexRequestChunking.MaxRequestBodyBytes}");
        }
    }

    [Fact]
    public void ChunkByJsonSize_RespectsMaxItemsPerRequest()
    {
        var items = Enumerable.Range(1, 250)
            .Select(i => MakeOffer($"SKU-{i}", descriptionChars: 20))
            .ToList();

        var chunks = YandexRequestChunking.ChunkByJsonSize(
            items,
            arrayPropertyName: "offerMappings");

        Assert.All(chunks, c => Assert.True(c.Count <= YandexRequestChunking.MaxItemsPerRequest));
        Assert.Equal(250, chunks.Sum(c => c.Count));
        Assert.True(chunks.Count >= 3);
    }

    [Fact]
    public void ChunkByJsonSize_SingleOversizedItem_StaysAlone()
    {
        var huge = MakeOffer("HUGE", descriptionChars: YandexRequestChunking.MaxRequestBodyBytes);
        var small = MakeOffer("SMALL", descriptionChars: 10);

        var chunks = YandexRequestChunking.ChunkByJsonSize(
            new[] { huge, small },
            arrayPropertyName: "offerMappings");

        Assert.Equal(2, chunks.Count);
        Assert.Single(chunks[0]);
        Assert.Equal("HUGE", chunks[0][0].Offer.OfferId);
        Assert.Single(chunks[1]);
        Assert.Equal("SMALL", chunks[1][0].Offer.OfferId);
    }

    [Fact]
    public void ChunkByJsonSize_Empty_ReturnsEmpty()
    {
        var chunks = YandexRequestChunking.ChunkByJsonSize(
            Array.Empty<UpdateOfferMappingDto>(),
            arrayPropertyName: "offerMappings");

        Assert.Empty(chunks);
    }

    [Fact]
    public void IsRequestTooBigError_DetectsPartnerApiMessage()
    {
        Assert.True(YandexRequestChunking.IsRequestTooBigError(
            "Yandex Market API error: BadRequest: {\"errors\":[{\"code\":\"BAD_REQUEST\",\"message\":\"The request is too big: Maximum allowed request size must be less than 500 KB, but passed 646 KB\"}]}"));
        Assert.False(YandexRequestChunking.IsRequestTooBigError("some other error"));
    }
}
