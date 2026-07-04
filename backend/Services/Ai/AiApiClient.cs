using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Models.Ai;

namespace Delegat.Services.Ai;

public class AiApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AiApiClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AiApiClient(HttpClient http, ILogger<AiApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AiResponse> SendAsync(AiModelConfig model, AiRequest request, CancellationToken ct)
    {
        request.model = model.modelId;
        request.stream = false;

        using var req = BuildRequest(model, request);
        using var resp = await SendWithRetryAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (model.provider == AiModelProvider.Anthropic)
            return JsonSerializer.Deserialize<AiResponse>(body, _jsonOptions)!;

        return ConvertOpenAiResponse(body);
    }

    public async IAsyncEnumerable<AiStreamEvent> StreamAsync(
        AiModelConfig model, AiRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        request.model = model.modelId;
        request.stream = true;

        using var req = BuildRequest(model, request);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("AI API error {Status}: {Body}", resp.StatusCode, body);
            yield return new AiStreamEvent { type = "error" };
            yield break;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        if (model.provider == AiModelProvider.Anthropic)
        {
            await foreach (var ev in ReadAnthropicSseAsync(reader, ct))
                yield return ev;
        }
        else
        {
            await foreach (var ev in ReadOpenAiSseAsync(reader, ct))
                yield return ev;
        }
    }

    private HttpRequestMessage BuildRequest(AiModelConfig model, AiRequest request)
    {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var req = new HttpRequestMessage(HttpMethod.Post, model.apiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (model.provider == AiModelProvider.Anthropic)
        {
            req.Headers.Add("x-api-key", model.apiKeyEncrypted); // decrypted before passing
            req.Headers.Add("anthropic-version", "2023-06-01");
        }
        else
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.apiKeyEncrypted);
        }

        return req;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage req, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                // Clone request since HttpRequestMessage can't be reused
                var clone = await CloneRequestAsync(req);
                req = clone;
            }

            var resp = await _http.SendAsync(req, ct);

            if ((int)resp.StatusCode is 429 or 529 && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct);
                resp.Dispose();
                continue;
            }

            return resp;
        }

        return await _http.SendAsync(req, ct);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);
        if (req.Content != null)
        {
            var body = await req.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(body);
            foreach (var h in req.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        foreach (var h in req.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return clone;
    }

    private static async IAsyncEnumerable<AiStreamEvent> ReadAnthropicSseAsync(
        StreamReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            AiStreamEvent? ev;
            try { ev = JsonSerializer.Deserialize<AiStreamEvent>(data, _jsonOptions); }
            catch { continue; }

            if (ev != null) yield return ev;
        }
    }

    private static async IAsyncEnumerable<AiStreamEvent> ReadOpenAiSseAsync(
        StreamReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            // Convert OpenAI delta to Anthropic-compatible event
            AiStreamEvent? ev = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0) continue;

                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    ev = new AiStreamEvent
                    {
                        type = "content_block_delta",
                        delta = new AiStreamDelta { type = "text_delta", text = content.GetString() }
                    };
                }
                else if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    var tc = toolCalls[0];
                    var argsDelta = tc.TryGetProperty("function", out var func) && func.TryGetProperty("arguments", out var args)
                        ? args.GetString() : null;
                    ev = new AiStreamEvent
                    {
                        type = "content_block_delta",
                        delta = new AiStreamDelta { type = "input_json_delta", partial_json = argsDelta }
                    };
                }

                var finishReason = choices[0].TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
                if (finishReason != null)
                    ev = new AiStreamEvent { type = "message_stop" };
            }
            catch { continue; }

            if (ev != null) yield return ev;
        }
    }

    private static AiResponse ConvertOpenAiResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var choices = root.GetProperty("choices");
        var message = choices[0].GetProperty("message");

        var blocks = new List<AiContentBlock>();

        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            blocks.Add(new AiContentBlock { type = "text", text = content.GetString() });

        if (message.TryGetProperty("tool_calls", out var toolCalls))
        {
            foreach (var tc in toolCalls.EnumerateArray())
            {
                var func = tc.GetProperty("function");
                var argsJson = func.GetProperty("arguments").GetString() ?? "{}";
                var inputEl = JsonDocument.Parse(argsJson).RootElement;
                blocks.Add(new AiContentBlock
                {
                    type = "tool_use",
                    id = tc.GetProperty("id").GetString(),
                    name = func.GetProperty("name").GetString(),
                    input = inputEl
                });
            }
        }

        var stopReason = choices[0].TryGetProperty("finish_reason", out var fr) ? fr.GetString() ?? "end_turn" : "end_turn";
        return new AiResponse { id = "", stop_reason = stopReason, content = blocks };
    }
}
