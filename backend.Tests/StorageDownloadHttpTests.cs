using System.Text;
using Delegat.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public sealed class StorageDownloadHttpTests
{
    [Theory]
    [InlineData("bytes=2-4", StorageDownloadHttp.RangeKind.Partial, 2, 3, 4)]
    [InlineData("bytes=5-", StorageDownloadHttp.RangeKind.Partial, 5, 5, 9)]
    [InlineData("bytes=-3", StorageDownloadHttp.RangeKind.Partial, 7, 3, 9)]
    [InlineData("bytes=10-", StorageDownloadHttp.RangeKind.Unsatisfiable, 0, 0, 0)]
    [InlineData("bytes=-0", StorageDownloadHttp.RangeKind.Unsatisfiable, 0, 0, 0)]
    [InlineData("bytes=8-2", StorageDownloadHttp.RangeKind.Unsatisfiable, 0, 0, 0)]
    [InlineData("bytes=bad-2", StorageDownloadHttp.RangeKind.Ignore, 0, 0, 0)]
    [InlineData("bytes=1-2,4-5", StorageDownloadHttp.RangeKind.Ignore, 0, 0, 0)]
    public void ParseRange_HandlesSingleRangesAndInvalidRequests(
        string header,
        StorageDownloadHttp.RangeKind kind,
        long offset,
        long length,
        long end)
    {
        var result = StorageDownloadHttp.ParseRange(header, 10);

        Assert.Equal(kind, result.Kind);
        if (kind == StorageDownloadHttp.RangeKind.Partial)
        {
            Assert.Equal(offset, result.Range.Offset);
            Assert.Equal(length, result.Range.Length);
            Assert.Equal(end, result.Range.End);
        }
    }

    [Fact]
    public async Task StreamAsync_ReturnsExactPartialContent()
    {
        var context = NewContext("GET", "bytes=2-4");
        var storage = new MemoryStorage(Encoding.UTF8.GetBytes("0123456789"));

        await StorageDownloadHttp.StreamAsync(
            context.Request, context.Response, storage, "object", "video/mp4", "clip.mp4",
            inline: true, contentLength: 10);

        Assert.Equal(StatusCodes.Status206PartialContent, context.Response.StatusCode);
        Assert.Equal("bytes 2-4/10", context.Response.Headers.ContentRange);
        Assert.Equal(3, context.Response.ContentLength);
        Assert.Equal("234", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    [Fact]
    public async Task StreamAsync_416IncludesUnsatisfiedContentRangeWithoutReadingObject()
    {
        var context = NewContext("GET", "bytes=20-");
        var storage = new MemoryStorage(Encoding.UTF8.GetBytes("0123456789"));

        await StorageDownloadHttp.StreamAsync(
            context.Request, context.Response, storage, "object", "video/mp4", "clip.mp4",
            inline: true, contentLength: 10);

        Assert.Equal(StatusCodes.Status416RangeNotSatisfiable, context.Response.StatusCode);
        Assert.Equal("bytes */10", context.Response.Headers.ContentRange);
        Assert.Equal(0, context.Response.ContentLength);
        Assert.Equal(0, storage.ReadCount);
    }

    [Fact]
    public async Task StreamAsync_HeadSetsRangeHeadersWithoutStreamingBytes()
    {
        var context = NewContext("HEAD", "bytes=-2");
        var storage = new MemoryStorage(Encoding.UTF8.GetBytes("0123456789"));

        await StorageDownloadHttp.StreamAsync(
            context.Request, context.Response, storage, "object", "video/mp4", "clip.mp4",
            inline: true, contentLength: 10);

        Assert.Equal(StatusCodes.Status206PartialContent, context.Response.StatusCode);
        Assert.Equal("bytes 8-9/10", context.Response.Headers.ContentRange);
        Assert.Equal(2, context.Response.ContentLength);
        Assert.Equal(0, storage.ReadCount);
        Assert.Equal(0, ((MemoryStream)context.Response.Body).Length);
    }

    [Fact]
    public async Task StreamAsync_EmptyObjectReturns200WithZeroLengthWhenNoRange()
    {
        var context = NewContext("GET", null);
        var storage = new MemoryStorage([]);

        await StorageDownloadHttp.StreamAsync(
            context.Request, context.Response, storage, "object", "application/octet-stream", "empty",
            inline: false, contentLength: 0);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.ContentLength);
        Assert.Equal(1, storage.ReadCount);
    }

    private static DefaultHttpContext NewContext(string method, string? range)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (range != null) context.Request.Headers.Range = range;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class MemoryStorage(byte[] bytes) : IStorageService
    {
        public string Bucket => "tests";
        public int ReadCount { get; private set; }
        public Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(true);
        public Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public string GetFullPath(string path) => path;
        public async Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default)
        {
            ReadCount++;
            await outputStream.WriteAsync(bytes, ct);
        }
        public async Task StreamRangeToAsync(string path, long offset, long length, Stream outputStream, CancellationToken ct = default)
        {
            ReadCount++;
            await outputStream.WriteAsync(bytes.AsMemory((int)offset, (int)length), ct);
        }
        public Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600) => throw new NotImplementedException();
        public Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
