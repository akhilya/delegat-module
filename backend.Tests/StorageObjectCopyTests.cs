using System.Net;
using System.Net.Http;
using Delegat.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class StorageObjectCopyTests
{
    [Fact]
    public async Task ResumesFromTheLastByteWhenStorageClosesEarly()
    {
        var payload = "hello world"u8.ToArray();
        var calls = new List<long>();
        var destination = new MemoryStream();

        var copied = await StorageObjectCopy.CopyAsync(
            (offset, _) =>
            {
                calls.Add(offset);
                if (offset == 0)
                    return Task.FromResult(ObjectResponse(HttpStatusCode.OK, payload, cutAt: 5));
                return Task.FromResult(ObjectResponse(HttpStatusCode.PartialContent, payload.AsSpan(5).ToArray()));
            },
            destination,
            NullLogger.Instance,
            "files/4719",
            CancellationToken.None);

        Assert.True(copied);
        Assert.Equal(new long[] { 0, 5 }, calls);
        Assert.Equal(payload, destination.ToArray());
    }

    [Fact]
    public async Task ResumesWhenTheBodyEndsBeforeContentLength()
    {
        var head = "abc"u8.ToArray();
        var tail = "def"u8.ToArray();
        var destination = new MemoryStream();

        await StorageObjectCopy.CopyAsync(
            (offset, _) =>
            {
                if (offset == 0)
                {
                    var response = ObjectResponse(HttpStatusCode.OK, head);
                    response.Content.Headers.ContentLength = head.Length + tail.Length;
                    return Task.FromResult(response);
                }

                return Task.FromResult(ObjectResponse(HttpStatusCode.PartialContent, tail));
            },
            destination,
            NullLogger.Instance,
            "files/4719",
            CancellationToken.None);

        Assert.Equal("abcdef"u8.ToArray(), destination.ToArray());
    }

    [Fact]
    public async Task DoesNotAppendTheWholeObjectWhenRangeIsIgnored()
    {
        var payload = "hello world"u8.ToArray();
        var destination = new MemoryStream();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => StorageObjectCopy.CopyAsync(
            (offset, _) => Task.FromResult(offset == 0
                ? ObjectResponse(HttpStatusCode.OK, payload, cutAt: 5)
                : ObjectResponse(HttpStatusCode.OK, payload)),
            destination,
            NullLogger.Instance,
            "files/4719",
            CancellationToken.None));

        Assert.Contains("did not resume", error.Message);
        Assert.Equal(payload.AsSpan(0, 5).ToArray(), destination.ToArray());
    }

    private static HttpResponseMessage ObjectResponse(HttpStatusCode status, byte[] body, int? cutAt = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StreamContent(cutAt is int cut ? new CutStream(body, cut) : new MemoryStream(body))
        };
        response.Content.Headers.ContentLength = body.Length;
        return response;
    }

    private sealed class CutStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _cutAt;
        private int _position;

        public CutStream(byte[] data, int cutAt)
        {
            _data = data;
            _cutAt = cutAt;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _cutAt)
                throw new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely. (ResponseEnded)");

            var count = Math.Min(buffer.Length, _cutAt - _position);
            _data.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            return new ValueTask<int>(count);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
