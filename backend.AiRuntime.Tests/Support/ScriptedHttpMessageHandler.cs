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
    /// Returns headers and <paramref name="prefixSse"/> promptly, then blocks mid-body —
    /// exercises cancellation while the SDK is inside the SSE read, not just at headers.
    /// </summary>
    public void EnqueueHangingMidStream(string prefixSse, TimeSpan hold) =>
        _steps.Enqueue((req, ct) =>
        {
            var content = new HangingSseContent(prefixSse, hold, ct);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = req,
                Content = content,
            });
        });

    private sealed class HangingSseContent(string prefix, TimeSpan hold, CancellationToken requestCt) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeBody(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            SerializeBody(stream, cancellationToken);

        private async Task SerializeBody(Stream stream, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCt);
            var bytes = Encoding.UTF8.GetBytes(prefix);
            await stream.WriteAsync(bytes, linked.Token).ConfigureAwait(false);
            await stream.FlushAsync(linked.Token).ConfigureAwait(false);
            await Task.Delay(hold, linked.Token).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
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
