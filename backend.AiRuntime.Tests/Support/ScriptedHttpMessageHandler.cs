using System.Net;
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
