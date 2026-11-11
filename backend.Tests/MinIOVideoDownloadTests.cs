using System.Net;
using System.Net.Sockets;
using System.Text;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public sealed class MinIOVideoDownloadTests
{
    private const string PublicSourceUrl = "http://93.184.216.34/video.mp4";

    [Fact]
    public async Task VideoDownload_StreamsDirectMp4ToMinio()
    {
        await using var minio = await MinioStub.StartAsync();
        var source = Mp4Bytes();
        var sourceRequests = 0;
        var storage = CreateStorage(minio, _ =>
        {
            sourceRequests++;
            return Response(source, "video/mp4");
        });

        var result = await storage.UploadVideoFromUriWithMetadataAsync(
            PublicSourceUrl, "products/1/video.mp4", maxBytes: 1024);

        Assert.Equal(new FileUploadResult("video/mp4", source.Length), result);
        Assert.Equal(1, sourceRequests);
        Assert.Contains(minio.Requests, request => request.Method == "PUT" && request.Path.EndsWith("/products/1/video.mp4"));
    }

    [Fact]
    public async Task VideoDownload_RejectsDeclaredOversizeBeforeReadingOrUploading()
    {
        var sourceRequests = 0;
        var source = Enumerable.Repeat((byte)1, 32).ToArray();
        var storage = CreateStorage(null, _ =>
        {
            sourceRequests++;
            return Response(source, "video/mp4");
        });

        var result = await storage.UploadVideoFromUriWithMetadataAsync(
            PublicSourceUrl, "products/1/large.mp4", maxBytes: 16);

        Assert.Null(result);
        Assert.Equal(1, sourceRequests);
    }

    [Fact]
    public async Task VideoDownload_RejectsRedirectToPrivateAddressBeforeFollowingIt()
    {
        var requests = new List<string>();
        var storage = CreateStorage(null, request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://127.0.0.1/private/video.mp4");
            return response;
        });

        var result = await storage.UploadVideoFromUriWithMetadataAsync(
            PublicSourceUrl, "products/1/redirect.mp4", maxBytes: 1024);

        Assert.Null(result);
        Assert.Equal([PublicSourceUrl], requests);
    }

    [Fact]
    public async Task VideoDownload_RejectsUnknownLengthStreamThatExceedsLimitAndDeletesPartialObject()
    {
        await using var minio = await MinioStub.StartAsync();
        var source = Mp4Bytes().Concat(new byte[] { 0x7f }).ToArray();
        var storage = CreateStorage(minio, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableReadStream(source))
        });

        var result = await storage.UploadVideoFromUriWithMetadataAsync(
            PublicSourceUrl, "products/1/unknown-length.mp4", maxBytes: Mp4Bytes().Length);

        Assert.Null(result);
        Assert.Contains(minio.Requests, request => request.Method == "DELETE" && request.Path.EndsWith("/products/1/unknown-length.mp4"));
        Assert.DoesNotContain(minio.Requests, request => request.Method == "PUT" && request.Path.EndsWith("/products/1/unknown-length.mp4"));
    }

    [Fact]
    public async Task VideoDownload_RethrowsCancellationAndDeletesPartialObject()
    {
        await using var minio = await MinioStub.StartAsync();
        using var cancellation = new CancellationTokenSource();
        var source = Mp4Bytes();
        var storage = CreateStorage(minio, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new CancelAfterReadStream(source, cancellation))
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.UploadVideoFromUriWithMetadataAsync(
                PublicSourceUrl, "products/1/cancelled.mp4", maxBytes: 1024, cancellation.Token));

        Assert.Contains(minio.Requests, request => request.Method == "DELETE" && request.Path.EndsWith("/products/1/cancelled.mp4"));
    }

    private static MinIOStorageService CreateStorage(
        MinioStub? minio,
        Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var endpoint = minio?.Endpoint ?? "127.0.0.1:9000";
        var settings = new MinIOSettings
        {
            Endpoint = endpoint,
            Bucket = "test-bucket",
            AccessKey = "test-access",
            SecretKey = "test-secret",
            Secure = false
        };
        return new MinIOStorageService(
            settings,
            new FakeHttpClientFactory(send),
            NullLogger<MinIOStorageService>.Instance);
    }

    private static HttpResponseMessage Response(byte[] bytes, string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }

    private static byte[] Mp4Bytes() => [0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0, 0, 0, 0, 0];

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> send)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new DelegateHandler(send), disposeHandler: true);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }

    private sealed class CancelAfterReadStream(byte[] bytes, CancellationTokenSource cancellation)
        : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read > 0)
                cancellation.Cancel();
            return read;
        }
    }

    private sealed class MinioStub : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string Endpoint { get; }
        public List<(string Method, string Path)> Requests { get; } = [];

        private MinioStub(WebApplication app, string endpoint)
        {
            _app = app;
            Endpoint = endpoint;
        }

        public static async Task<MinioStub> StartAsync()
        {
            using var portProbe = new TcpListener(IPAddress.Loopback, 0);
            portProbe.Start();
            var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            var app = builder.Build();
            var stub = new MinioStub(app, $"127.0.0.1:{port}");
            app.Run(async context =>
            {
                stub.Requests.Add((context.Request.Method, context.Request.Path.Value ?? ""));
                if (context.Request.Method == HttpMethods.Put)
                    context.Response.Headers.ETag = "\"fake-etag\"";
                context.Response.StatusCode = context.Request.Method == HttpMethods.Delete
                    ? StatusCodes.Status204NoContent
                    : StatusCodes.Status200OK;
                context.Response.ContentLength = 0;
                await context.Response.CompleteAsync();
            });
            await app.StartAsync();
            return stub;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
