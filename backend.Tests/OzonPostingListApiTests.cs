using System.Text.Json;
using Delegat.Marketplace.Ozon.Models;

namespace Delegat.Tests;

public class OzonPostingListApiTests
{
    [Fact]
    public void FbsV4Response_ReadsTopLevelPostingsAndCursor()
    {
        var response = JsonSerializer.Deserialize<OzonFbsPostingListResponse>("""
            {
              "postings": [{ "posting_number": "fbs-1", "status": "awaiting_deliver" }],
              "has_next": true,
              "cursor": "fbs-next"
            }
            """)!;

        Assert.Equal("fbs-1", Assert.Single(response.GetPostings()).PostingNumber);
        Assert.True(response.HasMore);
        Assert.Equal("fbs-next", response.Cursor);
    }

    [Fact]
    public void FboV3Response_ReadsTopLevelPostingsAndCursor()
    {
        var response = JsonSerializer.Deserialize<OzonFboPostingListResponse>("""
            {
              "postings": [{ "posting_number": "fbo-1", "status": "delivering" }],
              "has_next": true,
              "cursor": "fbo-next"
            }
            """)!;

        Assert.Equal("fbo-1", Assert.Single(response.GetPostings()).PostingNumber);
        Assert.True(response.HasMore);
        Assert.Equal("fbo-next", response.GetCursor());
    }

    [Fact]
    public void FboV3Response_ReadsResultWrappedPostingsForCompatibility()
    {
        var response = JsonSerializer.Deserialize<OzonFboPostingListResponse>("""
            {
              "result": {
                "postings": [{ "posting_number": "fbo-legacy", "status": "delivered" }],
                "has_next": true,
                "cursor": "fbo-legacy-next"
              }
            }
            """)!;

        Assert.Equal("fbo-legacy", Assert.Single(response.GetPostings()).PostingNumber);
        Assert.True(response.HasMore);
        Assert.Equal("fbo-legacy-next", response.GetCursor());
    }

    [Fact]
    public void NewPostingListRequestsUseCursorsInsteadOfOffsets()
    {
        var fbs = JsonSerializer.Serialize(new OzonFbsPostingListRequest
        {
            Cursor = "fbs-cursor",
            Filter = new OzonPostingListFilter { Since = "from", To = "to", Status = "delivering" }
        });
        var fbo = JsonSerializer.Serialize(new OzonFboPostingListRequest
        {
            Cursor = "fbo-cursor",
            Filter = new OzonPostingListFilter { Since = "from", To = "to", Status = "delivering" }
        });

        using var fbsJson = JsonDocument.Parse(fbs);
        using var fboJson = JsonDocument.Parse(fbo);
        Assert.Equal("fbs-cursor", fbsJson.RootElement.GetProperty("cursor").GetString());
        Assert.Equal("fbo-cursor", fboJson.RootElement.GetProperty("cursor").GetString());
        Assert.False(fbsJson.RootElement.TryGetProperty("offset", out _));
        Assert.False(fboJson.RootElement.TryGetProperty("offset", out _));
        Assert.True(fboJson.RootElement.TryGetProperty("sort_dir", out _));
    }

    [Theory]
    [InlineData("15.25", "15.25")]
    [InlineData("\"15.25\"", "15.25")]
    [InlineData("{\"value\":15.25,\"currency_code\":\"RUB\"}", "15.25")]
    [InlineData("{\"amount\":{\"value\":15.25}}", "15.25")]
    public void PostingProductPrice_ReadsScalarOrStructuredPrice(string jsonPrice, string expected)
    {
        var posting = JsonSerializer.Deserialize<OzonFbsPosting>( $$"""
            { "products": [{ "price": {{jsonPrice}} }] }
            """)!;

        Assert.Equal(expected, Assert.Single(posting.Products!).Price);
    }

    [Fact]
    public void PostingProductPrice_UnknownObjectDoesNotBreakPostingDeserialization()
    {
        var posting = JsonSerializer.Deserialize<OzonFbsPosting>("""
            { "products": [{ "price": { "currency_code": "RUB" } }] }
            """)!;

        Assert.Null(Assert.Single(posting.Products!).Price);
    }
}
