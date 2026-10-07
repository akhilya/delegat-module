using System.Net.Http.Headers;
using Delegat.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public class UploadFormBindingTests
{
    [Fact]
    public async Task MvcReadsAMultipartBodyBeforeTheAction()
    {
        await using var host = await ProbeHost.StartAsync();
        using var client = new HttpClient();

        var response = await client.PutAsync(host.Url + "/plain?folderId=1", Multipart(new string('x', 100_000)));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {text}");
        Assert.Equal("0", text);
    }

    [Fact]
    public async Task StreamingUploadKeepsTheBodyWhenFormBindingIsDisabled()
    {
        await using var host = await ProbeHost.StartAsync();
        using var client = new HttpClient();

        var payload = new string('x', 100_000);
        var response = await client.PutAsync(host.Url + "/stream", Multipart(payload));
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {text}");
        Assert.Equal($"a.txt:{payload.Length}", text);
    }

    private static MultipartFormDataContent Multipart(string payload)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(payload));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "files", "a.txt");
        return content;
    }

    private sealed class ProbeHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string Url { get; }

        private ProbeHost(WebApplication app, string url)
        {
            _app = app;
            Url = url;
        }

        public static async Task<ProbeHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var mvc = builder.Services.AddControllers();
            mvc.ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(typeof(UploadBindingProbeController).Assembly));
            });
            var app = builder.Build();
            app.MapControllers();
            await app.StartAsync();
            var url = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();
            return new ProbeHost(app, url);
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}

[ApiController]
public sealed class UploadBindingProbeController : ControllerBase
{
        [HttpPut("/plain")]
        public async Task<string> Plain([FromQuery] int? folderId) => (await CountBody()).ToString();

    [HttpPut("/stream")]
    [DisableFormValueModelBinding]
    public async Task<string> Stream(CancellationToken ct)
    {
        var saved = await MultipartObjectUpload.SaveAsync(
            Request,
            new UploadBindingReadingStorage(),
            name => name,
            ct);
        var file = Assert.Single(saved);
        return file.FileName + ":" + file.Length;
    }

    private async Task<long> CountBody()
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await Request.Body.ReadAsync(buffer);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}

file sealed class UploadBindingReadingStorage : IStorageService
{
    public string Bucket => "test";
    public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => Task.FromResult(true);

    public async Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default)
    {
        var buffer = new byte[81920];
        while (await stream.ReadAsync(buffer, ct) > 0) { }
        return true;
    }

    public Task<FileUploadResult?> UploadStreamAsync(Stream stream, string path, string contentType, long? contentLength, CancellationToken ct = default)
        => ReadLength(stream, contentType, ct);

    private static async Task<FileUploadResult?> ReadLength(Stream stream, string contentType, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            total += read;
        }
        return new FileUploadResult(contentType, total);
    }

    public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult(true);
    public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => Task.FromResult<FileUploadResult?>(null);
    public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> DeleteAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(false);
    public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
    public string GetFullPath(string path) => path;
    public Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult("");
    public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => Task.FromResult("");
    public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => Task.FromResult(new List<string>());
}
