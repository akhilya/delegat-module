using System.Net;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.Ozon.Models;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Tests;

public sealed class OzonVideoMediaTests
{
    [Fact]
    public void ProductImportBuildsOrderedTypedVideoAttributesAndDoesNotUseCoverIds()
    {
        var item = OzonPlugin.BuildImportItem(new Dictionary<string, object>
        {
            ["name"] = "Desk lamp",
            ["sku"] = "LAMP-1",
            ["videos"] = new List<string>
            {
                "https://media.example.test/first.mp4",
                "https://media.example.test/second.mp4"
            },
            ["videoNames"] = new List<string> { "front view.mp4", "detail view.mp4" }
        }, existingExternalId: null);

        var attributes = Assert.Single(item.ComplexAttributes!).Attributes;
        var videoUrls = Assert.Single(attributes, attribute => attribute.Id == 21841);
        var videoNames = Assert.Single(attributes, attribute => attribute.Id == 21837);

        Assert.All(attributes, attribute => Assert.Equal(100001, attribute.ComplexId));
        Assert.Equal(
            ["https://media.example.test/first.mp4", "https://media.example.test/second.mp4"],
            videoUrls.Values.Select(value => value.Value));
        Assert.Equal(["front view.mp4", "detail view.mp4"], videoNames.Values.Select(value => value.Value));
        Assert.DoesNotContain(attributes, attribute => attribute.Id == 21845 || attribute.ComplexId == 100002);
    }

    [Fact]
    public void ProductImportMapsOnlyCandidateVideoFilesAndKeepsThemOutOfImageUrls()
    {
        var attributes = new ProductAttributesItem
        {
            Id = 42,
            ComplexAttributes =
            [
                new ComplexAttributeInfo
                {
                    Attributes =
                    [
                        Attribute(100001, 21841,
                            "https://www.youtube.com/watch?v=sample",
                            "https://cdn.example.test/direct.mp4",
                            "https://cdn.example.test/stream.m3u8"),
                        Attribute(100001, 21837, "player", "direct clip", "stream")
                    ]
                },
                new ComplexAttributeInfo
                {
                    Attributes = [Attribute(100002, 21845, "https://cdn.example.test/cover.mp4")]
                }
            ]
        };

        var product = OzonPlugin.MapToExternalProduct(
            new ProductInfoItem { Id = 42, OfferId = "LAMP-1", Name = "Desk lamp" },
            attributes);

        Assert.Null(product.ImageUrls);
        var media = Assert.Single(product.Media!);
        Assert.Equal("video", media.Kind);
        Assert.Equal("https://cdn.example.test/direct.mp4", media.Url);
        Assert.Equal(1, media.Index);
        Assert.Equal("direct clip", media.FileName);
    }

    [Fact]
    public async Task ProductImportMapsV4ComplexAttributesUsingLegacyAttributeId()
    {
        var attributes = await DeserializeProductAttributesAsync("attribute_id");

        Assert.Equal(901, Assert.Single(attributes.Attributes).AttributeId);
        var product = OzonPlugin.MapToExternalProduct(
            new ProductInfoItem { Id = 42, OfferId = "LAMP-1", Name = "Desk lamp" },
            attributes);

        var media = Assert.Single(product.Media!);
        Assert.Equal("https://cdn1.ozone.ru/s3/multimedia-video/clip.mp4", media.Url);
        Assert.Equal("Clip", media.FileName);

        var serialized = JsonSerializer.Serialize(attributes);
        Assert.Contains("\"id\":21841", serialized);
        Assert.DoesNotContain("\"attribute_id\"", serialized);
    }

    [Fact]
    public async Task ProductImportMapsV4ComplexAttributesUsingId()
    {
        var attributes = await DeserializeProductAttributesAsync("id");

        Assert.Equal(901, Assert.Single(attributes.Attributes).AttributeId);
        var product = OzonPlugin.MapToExternalProduct(
            new ProductInfoItem { Id = 42, OfferId = "LAMP-1", Name = "Desk lamp" },
            attributes);

        var media = Assert.Single(product.Media!);
        Assert.Equal("https://cdn1.ozone.ru/s3/multimedia-video/clip.mp4", media.Url);
        Assert.Equal("Clip", media.FileName);
    }

    [Fact]
    public void ProductImportMapsEveryVideoFromSeparateComplexAttributeGroups()
    {
        var attributes = new ProductAttributesItem
        {
            Id = 42,
            ComplexAttributes =
            [
                new ComplexAttributeInfo
                {
                    Attributes =
                    [
                        Attribute(100001, 21841, "https://cdn.example.test/first.mp4"),
                        Attribute(100001, 21837, "first clip")
                    ]
                },
                new ComplexAttributeInfo
                {
                    Attributes =
                    [
                        Attribute(100001, 21837, "second clip"),
                        Attribute(100001, 21841, "https://cdn.example.test/second.mp4")
                    ]
                }
            ]
        };

        var product = OzonPlugin.MapToExternalProduct(
            new ProductInfoItem { Id = 42, OfferId = "LAMP-1", Name = "Desk lamp" },
            attributes);

        Assert.Equal(
            ["https://cdn.example.test/first.mp4", "https://cdn.example.test/second.mp4"],
            product.Media!.Select(media => media.Url));
        Assert.Equal(["first clip", "second clip"], product.Media!.Select(media => media.FileName));
        Assert.Equal([0, 1], product.Media!.Select(media => media.Index));
    }

    [Fact]
    public void ProductImportReadsGalleryVideoFromTopLevelAttributesWithoutTextUrlValues()
    {
        var attributes = new ProductAttributesItem
        {
            Id = 42,
            Attributes =
            [
                Attribute(100001, 21841, "https://cdn.example.test/clip.mp4"),
                Attribute(100001, 21837, "clip"),
                Attribute(100002, 21845, "https://cdn.example.test/cover.mp4")
            ]
        };

        var product = OzonPlugin.MapToExternalProduct(
            new ProductInfoItem { Id = 42, OfferId = "LAMP-1", Name = "Desk lamp" },
            attributes);

        var media = Assert.Single(product.Media!);
        Assert.Equal("https://cdn.example.test/clip.mp4", media.Url);
        Assert.Equal("clip", media.FileName);
        Assert.False(product.Attributes?.ContainsKey("21841") ?? false);
        Assert.False(product.Attributes?.ContainsKey("21845") ?? false);
    }

    [Fact]
    public void EmptyLocalMediaPreservesRemoteGalleryAndCoverWhileSelectedVideoReplacesOnlyGalleryVideo()
    {
        var existing = ExistingAttributes();
        var emptyLocal = OzonPlugin.BuildImportItem(new Dictionary<string, object>
        {
            ["name"] = "Desk lamp",
            ["sku"] = "LAMP-1"
        }, existingExternalId: "42");

        OzonPlugin.PreserveExistingMedia(emptyLocal, existing);

        Assert.Equal(["https://cdn.example.test/primary.jpg", "https://cdn.example.test/second.jpg"], emptyLocal.Images);
        Assert.Equal("https://cdn.example.test/primary.jpg", emptyLocal.PrimaryImage);
        Assert.Contains(emptyLocal.ComplexAttributes!.SelectMany(group => group.Attributes),
            attribute => attribute.Id == 21841 && Assert.Single(attribute.Values).Value == "https://cdn.example.test/old.mp4");
        Assert.Contains(emptyLocal.ComplexAttributes!.SelectMany(group => group.Attributes),
            attribute => attribute.ComplexId == 100002 && attribute.Id == 21845);

        var update = OzonPlugin.BuildImportItem(new Dictionary<string, object>
        {
            ["name"] = "Desk lamp",
            ["sku"] = "LAMP-1",
            ["videos"] = new List<string> { "https://media.example.test/new.mp4" }
        }, existingExternalId: "42");
        OzonPlugin.PreserveExistingMedia(update, existing);

        var attributes = update.ComplexAttributes!.SelectMany(group => group.Attributes).ToList();
        Assert.Equal("https://media.example.test/new.mp4",
            Assert.Single(attributes, attribute => attribute.ComplexId == 100001 && attribute.Id == 21841).Values[0].Value);
        Assert.Contains(attributes, attribute => attribute.ComplexId == 100002 && attribute.Id == 21845);
    }

    [Fact]
    public async Task OzonImportClientPostsVerifiedVideoComplexAttributeShape()
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
                Content = new StringContent("{\"result\":{\"task_id\":77}}", Encoding.UTF8, "application/json")
            });
        }))
        {
            BaseAddress = new Uri("https://api-seller.ozon.ru/")
        };
        var client = new OzonApiClient("fake-client", "fake-key", http);
        var importItem = OzonPlugin.BuildImportItem(new Dictionary<string, object>
        {
            ["name"] = "Desk lamp",
            ["sku"] = "LAMP-1",
            ["videos"] = new List<string> { "https://media.example.test/clip.mp4" }
        }, existingExternalId: null);

        var response = await client.ImportProductsAsync([importItem]);

        Assert.Equal(77, response.Result.TaskId);
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("/v3/product/import", capturedPath);
        using var json = JsonDocument.Parse(capturedBody!);
        var requestItem = json.RootElement.GetProperty("items")[0];
        var complexAttributes = requestItem.GetProperty("complex_attributes")[0].GetProperty("attributes");
        Assert.Equal(100001, complexAttributes[0].GetProperty("complex_id").GetInt32());
        Assert.Equal(21841, complexAttributes[0].GetProperty("id").GetInt32());
        Assert.Equal("https://media.example.test/clip.mp4",
            complexAttributes[0].GetProperty("values")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task SingleExportFailsClosedWhenOfferPrecheckCannotConfirmExistingCardState()
    {
        var requestedPaths = new List<string>();
        using var http = CreateFailingHttpClient(requestedPaths);
        var client = new OzonApiClient("fake-client", "fake-key", http);
        var plugin = new OzonPlugin(null!, _ => client);

        var result = await plugin.ExportProductAsync(
            new ConnectionContext(),
            new Dictionary<string, object> { ["name"] = "Desk lamp", ["sku"] = "LAMP-1" });

        Assert.False(result.Success);
        Assert.Contains("could not verify whether this offer already exists", result.ErrorMessage);
        Assert.Equal(["/v3/product/info/list"], requestedPaths);
    }

    [Fact]
    public async Task BulkExportFailsClosedWhenOfferPrecheckCannotConfirmExistingCardState()
    {
        var requestedPaths = new List<string>();
        using var http = CreateFailingHttpClient(requestedPaths);
        var client = new OzonApiClient("fake-client", "fake-key", http);
        var plugin = new OzonPlugin(null!, _ => client);

        var result = await plugin.ExportProductsBulkAsync(
            new ConnectionContext(),
            [new BulkExportItem(
                91,
                new Dictionary<string, object> { ["name"] = "Desk lamp", ["sku"] = "LAMP-1" })]);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Contains("current media could not be loaded", Assert.Single(result.Items!).ErrorMessage);
        Assert.Equal(["/v3/product/info/list"], requestedPaths);
    }

    private static HttpClient CreateFailingHttpClient(List<string> requestedPaths)
        => new(new CaptureHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"message\":\"fake pre-check failure\"}", Encoding.UTF8, "application/json")
            });
        }))
        {
            BaseAddress = new Uri("https://api-seller.ozon.ru/")
        };

    private static ProductAttributeInfo Attribute(int complexId, long id, params string[] values)
        => new()
        {
            ComplexId = complexId,
            AttributeId = id,
            Values = values.Select(value => new AttributeValueInfo { Value = value }).ToList()
        };

    private static async Task<ProductAttributesItem> DeserializeProductAttributesAsync(
        string complexAttributeIdProperty)
    {
        var responseJson = $$"""
            {
              "result": [
                {
                  "id": 42,
                  "offer_id": "LAMP-1",
                  "attributes": [
                    {
                      "id": 901,
                      "complex_id": 0,
                      "values": [{ "value": "top-level" }]
                    }
                  ],
                  "complex_attributes": [
                    {
                      "attributes": [
                        {
                          "{{complexAttributeIdProperty}}": 21841,
                          "complex_id": 100001,
                          "values": [{ "value": "https://cdn1.ozone.ru/s3/multimedia-video/clip.mp4" }]
                        },
                        {
                          "{{complexAttributeIdProperty}}": 21837,
                          "complex_id": 100001,
                          "values": [{ "value": "Clip" }]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        using var http = new HttpClient(new CaptureHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        })))
        {
            BaseAddress = new Uri("https://api-seller.ozon.ru/")
        };
        var client = new OzonApiClient("fake-client", "fake-key", http);
        var response = await client.GetProductAttributesAsync(productIds: ["42"]);
        return Assert.Single(response.Result);
    }

    private static ProductAttributesItem ExistingAttributes() => new()
    {
        Id = 42,
        Images = ["https://cdn.example.test/primary.jpg", "https://cdn.example.test/second.jpg"],
        PrimaryImage = "https://cdn.example.test/primary.jpg",
        ComplexAttributes =
        [
            new ComplexAttributeInfo
            {
                Attributes =
                [
                    Attribute(100001, 21841, "https://cdn.example.test/old.mp4"),
                    Attribute(100001, 21837, "old clip")
                ]
            },
            new ComplexAttributeInfo
            {
                Attributes = [Attribute(100002, 21845, "https://cdn.example.test/cover.mp4")]
            }
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
