using Delegat.Infrastructure.Storage;
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
}
