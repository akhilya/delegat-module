using Delegat.Infrastructure.Storage;

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
}
