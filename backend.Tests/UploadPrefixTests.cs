using System.Text;
using Delegat.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Delegat.Tests;

public class UploadPrefixTests
{
    [Fact]
    public async Task SmallUploadFitsOnePut()
    {
        var payload = "photo"u8.ToArray();
        await using var stream = new MemoryStream(payload);

        var (data, fitsSinglePut) = await UploadPrefix.ReadAsync(stream, CancellationToken.None);

        Assert.True(fitsSinglePut);
        Assert.Equal(payload, data);
    }

    [Fact]
    public async Task LargeUploadKeepsThePrefixAndTheTail()
    {
        var payload = new byte[UploadPrefix.SinglePutMaxBytes + 8];
        payload[0] = 1;
        payload[^1] = 9;
        await using var stream = new MemoryStream(payload);

        var (prefix, fitsSinglePut) = await UploadPrefix.ReadAsync(stream, CancellationToken.None);

        Assert.False(fitsSinglePut);
        Assert.Equal(UploadPrefix.SinglePutMaxBytes + 1, prefix.Length);

        await using var combined = new PrefixThenRemainingStream(prefix, stream);
        await using var copy = new MemoryStream();
        await combined.CopyToAsync(copy);

        Assert.Equal(payload, copy.ToArray());
    }

    [Fact]
    public async Task MultipartSectionCanBeReadThroughToTheClosingBoundary()
    {
        const string boundary = "----upload-boundary";
        var payload = "file-bytes"u8.ToArray();
        var head = System.Text.Encoding.UTF8.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"a.txt\"\r\nContent-Type: text/plain\r\n\r\n");
        var tail = System.Text.Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
        await using var body = new MemoryStream();
        await body.WriteAsync(head);
        await body.WriteAsync(payload);
        await body.WriteAsync(tail);
        body.Position = 0;
        var reader = new MultipartReader(boundary, body);
        var section = await reader.ReadNextSectionAsync();
        Assert.NotNull(section);

        var (data, fitsSinglePut) = await UploadPrefix.ReadAsync(section!.Body, CancellationToken.None);
        Assert.True(fitsSinglePut);
        Assert.Equal(payload, data);
        Assert.Null(await reader.ReadNextSectionAsync());
    }

    [Fact]
    public async Task CompleteMultipartStillStoresTheFile()
    {
        const string boundary = "----upload-boundary";
        var payload = "file-bytes"u8.ToArray();
        var head = Encoding.UTF8.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"a.txt\"\r\nContent-Type: text/plain\r\n\r\n");
        var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
        var body = new MemoryStream();
        await body.WriteAsync(head);
        await body.WriteAsync(payload);
        await body.WriteAsync(tail);
        body.Position = 0;

        var http = new DefaultHttpContext();
        http.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        http.Request.ContentLength = body.Length;
        http.Request.Body = body;

        var saved = await MultipartObjectUpload.SaveAsync(
            http.Request,
            new StubStorage(),
            name => $"store/{name}",
            CancellationToken.None);

        Assert.Single(saved);
        Assert.Equal("a.txt", saved[0].FileName);
        Assert.Equal("store/a.txt", saved[0].Path);
    }

    [Fact]
    public async Task TruncatedUploadRecordsDeclaredLengthAndBytesReceived()
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = "multipart/form-data; boundary=----upload-boundary";
        http.Request.ContentLength = 154451349;
        http.Request.Body = new MemoryStream("xx"u8.ToArray());
        http.Request.Headers["Expect"] = "100-continue";

        var ex = await Assert.ThrowsAsync<IOException>(() =>
            MultipartObjectUpload.SaveAsync(http.Request, new StubStorage(), name => name, CancellationToken.None));

        Assert.Contains("Declared Content-Length: 154451349", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Bytes received: 2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Expect: 100-continue", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ClientAborted: False", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PrefixHex: 7878", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Unexpected end of Stream", ex.InnerException?.Message, StringComparison.Ordinal);
    }

    private sealed class StubStorage : IStorageService
    {
        public string Bucket => "test";
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => Task.FromResult(true);
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
}
