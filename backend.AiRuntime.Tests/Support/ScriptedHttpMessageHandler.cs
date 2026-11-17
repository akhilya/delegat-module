using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Delegat.AiRuntime.Tests.Support;

/// <summary>
/// Deterministic transport fake for the M1 prototype: serves queued responses and records
/// every request body. The OpenAI and Anthropic SDKs are pointed at a fake base URL, so all
/// provider traffic terminates in this handler — no live calls, no API keys.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = new();

    public List<string> RequestBodies { get; } = new();
    public List<string> RequestUrls { get; } = new();

    public int RequestCount => RequestUrls.Count;

    public void EnqueueSse(string body) =>
        _steps.Enqueue((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = req,
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        }));

    public void EnqueueJson(string body) =>
        _steps.Enqueue((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = req,
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    public void EnqueueStatus(HttpStatusCode status, string body = "") =>
        _steps.Enqueue((req, ct) => Task.FromResult(new HttpResponseMessage(status)
        {
            RequestMessage = req,
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    /// <summary>
    /// Blocks inside SendAsync until <paramref name="hold"/> elapses or the request
    /// cancellation token fires — used to observe cancellation propagation.
    /// </summary>
    public void EnqueueHanging(TimeSpan hold) =>
        _steps.Enqueue(async (req, ct) =>
        {
            await Task.Delay(hold, ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = req,
                Content = new StringContent("data: [DONE]\n\n", Encoding.UTF8, "text/event-stream"),
            };
        });

    /// <summary>
    /// Returns headers and serves <paramref name="prefixSse"/> from an unbuffered stream,
    /// then blocks on the NEXT read — so the SDK's SSE parser is genuinely mid-read when
    /// cancellation fires, not stuck inside buffered HttpContent serialization.
    /// </summary>
    public void EnqueueHangingMidStream(string prefixSse, TimeSpan hold) =>
        _steps.Enqueue((req, ct) =>
        {
            var content = new HangingSseContent(prefixSse, hold, ct);
            _lastHangingContent = content;
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = req,
                Content = content,
            });
        });

    private HangingSseContent? _lastHangingContent;

    /// <summary>
    /// True once the SSE parser consumed the whole prefix before the stall — proof the
    /// request was cancelled while the parser was mid-read, not before any bytes arrived.
    /// </summary>
    public bool MidStreamPrefixFullyRead => _lastHangingContent?.PrefixFullyRead == true;

    private sealed class HangingSseContent : HttpContent
    {
        private readonly string _prefix;
        private readonly TimeSpan _hold;
        private readonly CancellationToken _requestCt;
        private HangingSseStream? _stream;

        public HangingSseContent(string prefix, TimeSpan hold, CancellationToken requestCt)
        {
            _prefix = prefix;
            _hold = hold;
            _requestCt = requestCt;
        }

        public bool PrefixFullyRead => _stream?.PrefixFullyRead == true;

        // Without this override ReadAsStreamAsync would buffer SerializeToStreamAsync
        // into a MemoryStream and the parser would never observe live bytes.
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            CreateContentReadStreamAsync(CancellationToken.None);

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _requestCt);
            _stream = new HangingSseStream(Encoding.UTF8.GetBytes(_prefix), _hold, linked);
            return Task.FromResult<Stream>(_stream);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await SerializeBody(stream, CancellationToken.None).ConfigureAwait(false);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            await SerializeBody(stream, cancellationToken).ConfigureAwait(false);

        private async Task SerializeBody(Stream stream, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _requestCt);
            var bytes = Encoding.UTF8.GetBytes(_prefix);
            await stream.WriteAsync(bytes, linked.Token).ConfigureAwait(false);
            await stream.FlushAsync(linked.Token).ConfigureAwait(false);
            await Task.Delay(_hold, linked.Token).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    /// <summary>
    /// Yields the prefix across reads, then blocks on the following read until cancelled.
    /// </summary>
    private sealed class HangingSseStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly TimeSpan _hold;
        private readonly CancellationTokenSource _cts;
        private int _served;

        public HangingSseStream(byte[] prefix, TimeSpan hold, CancellationTokenSource cts)
        {
            _prefix = prefix;
            _hold = hold;
            _cts = cts;
        }

        public bool PrefixFullyRead => _served >= _prefix.Length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_served < _prefix.Length)
            {
                var n = Math.Min(buffer.Length, _prefix.Length - _served);
                _prefix.AsSpan(_served, n).CopyTo(buffer.Span);
                _served += n;
                return n;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
            await Task.Delay(_hold, linked.Token).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cts.Dispose();
            base.Dispose(disposing);
        }
    }

    public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step) =>
        _steps.Enqueue(step);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUrls.Add(request.RequestUri?.ToString() ?? "");
        RequestBodies.Add(request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (_steps.Count == 0)
            throw new InvalidOperationException(
                $"Scripted handler ran out of queued responses after {RequestUrls.Count} requests " +
                $"(last: {RequestUrls[^1]}).");

        return await _steps.Dequeue()(request, cancellationToken).ConfigureAwait(false);
    }
}
