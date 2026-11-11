using System.Net;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.Wildberries;
using Delegat.Marketplace.Wildberries.Models;

namespace Delegat.Tests;

public sealed class WildberriesVideoMediaTests
{
    [Fact]
    public void BuildMediaUrls_PhotoOnlyUpdatePreservesExistingVideoAndPhotos()
    {
        var existing = ExistingCard();

        var urls = WildberriesPlugin.BuildMediaUrls(
            existing,
            ["https://cdn.example.test/new.jpg"],
            []);

        Assert.Equal(
        [
            "https://cdn.example.test/old-video.mp4",
            "https://cdn.example.test/old-1.jpg",
            "https://cdn.example.test/old-2.jpg",
            "https://cdn.example.test/new.jpg"
        ], urls);
    }

    [Fact]
    public void BuildMediaUrls_VideoOnlyUpdatePreservesPhotosAndReplacesVideo()
    {
        var existing = ExistingCard();

        var urls = WildberriesPlugin.BuildMediaUrls(
            existing,
            [],
            ["https://media.example.test/api/pub/media-export/new-token"]);

        Assert.Equal(
        [
            "https://media.example.test/api/pub/media-export/new-token",
            "https://cdn.example.test/old-1.jpg",
            "https://cdn.example.test/old-2.jpg"
        ], urls);
    }

    [Fact]
    public void EmptyLocalGalleryDoesNotRequestRemoteMediaReplacement()
    {
        Assert.False(WildberriesPlugin.HasLocalMedia([], []));
        Assert.True(WildberriesPlugin.HasLocalMedia([], ["https://media.example.test/clip.mp4"]));
    }

    [Fact]
    public void BuildMediaUrlsRejectsUnsupportedCountsRatherThanTruncating()
    {
        var thirtyOnePhotos = Enumerable.Range(0, 31)
            .Select(index => $"https://cdn.example.test/{index}.jpg")
            .ToList();

        Assert.Throws<InvalidOperationException>(() =>
            WildberriesPlugin.BuildMediaUrls(null, thirtyOnePhotos, []));
        Assert.Throws<InvalidOperationException>(() =>
            WildberriesPlugin.BuildMediaUrls(null, [],
            ["https://media.example.test/one.mp4", "https://media.example.test/two.mp4"]));
    }

    [Fact]
    public async Task SaveMediaByUrlAsyncSendsOneCompleteVideoFirstReplacementPayload()
    {
        string? capturedBody = null;
        string? capturedPath = null;
        HttpMethod? capturedMethod = null;
        using var http = new HttpClient(new CaptureHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedPath = request.RequestUri!.AbsolutePath;
            capturedMethod = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":null,\"error\":false,\"errorText\":\"\"}", Encoding.UTF8, "application/json")
            });
        }))
        {
            BaseAddress = new Uri("https://content-api.wildberries.ru/")
        };
        var client = new WildberriesApiClient("fake-media-token", http);
        var data = WildberriesPlugin.BuildMediaUrls(
            ExistingCard(),
            ["https://cdn.example.test/new.jpg"],
            ["https://media.example.test/api/pub/media-export/token"]);

        var response = await client.SaveMediaByUrlAsync(new WbMediaSaveRequest
        {
            NmId = 123456,
            Data = data
        });

        Assert.False(response.Error);
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("/content/v3/media/save", capturedPath);
        using var json = JsonDocument.Parse(capturedBody!);
        Assert.Equal(123456, json.RootElement.GetProperty("nmId").GetInt64());
        Assert.Equal(
            data,
            json.RootElement.GetProperty("data").EnumerateArray().Select(value => value.GetString()!).ToList());
        Assert.StartsWith("https://media.example.test/", data[0]);
        Assert.Equal(4, data.Count);
    }

    private static WbCard ExistingCard() => new()
    {
        NmId = 123456,
        Video = "https://cdn.example.test/old-video.mp4",
        Photos =
        [
            new WbPhoto { Big = "https://cdn.example.test/old-1.jpg" },
            new WbPhoto { Big = "https://cdn.example.test/old-2.jpg" }
        ]
    };

    private sealed class CaptureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => send(request);
    }
}
