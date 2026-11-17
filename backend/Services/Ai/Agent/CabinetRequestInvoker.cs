using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Delegat.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Holds the remaining middleware pipeline so <see cref="CabinetRequestInvoker"/>
/// can run cabinet controllers in-process (no loopback HTTP).
/// </summary>
public sealed class CabinetPipeline
{
    private RequestDelegate? _next;

    public void Capture(RequestDelegate next) => _next ??= next;

    public RequestDelegate? Next => _next;
}

public interface ICabinetRequestInvoker
{
    Task<(object result, string summary)> InvokeAsync(
        int storeId,
        int userId,
        string method,
        string apiPath,
        IReadOnlyDictionary<string, string> query,
        JsonElement? body,
        CancellationToken ct);
}

public sealed class CabinetRequestInvoker : ICabinetRequestInvoker
{
    public const string NestedItemKey = "delegat.agent.nested";
    public const string OriginApiKeyIdItemKey = "delegat.agent.originApiKeyId";
    public const int MaxResponseBytes = 256_000;
    public const int MaxListItems = 12;

    private static readonly JsonSerializerOptions JsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly CabinetPipeline _pipeline;
    private readonly IHttpContextAccessor _http;
    private readonly IJwtService _jwt;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CabinetRequestInvoker> _log;

    public CabinetRequestInvoker(
        CabinetPipeline pipeline,
        IHttpContextAccessor http,
        IJwtService jwt,
        IServiceScopeFactory scopes,
        ILogger<CabinetRequestInvoker> log)
    {
        _pipeline = pipeline;
        _http = http;
        _jwt = jwt;
        _scopes = scopes;
        _log = log;
    }

    public async Task<(object result, string summary)> InvokeAsync(
        int storeId,
        int userId,
        string method,
        string apiPath,
        IReadOnlyDictionary<string, string> query,
        JsonElement? body,
        CancellationToken ct)
    {
        var next = _pipeline.Next;
        if (next == null)
            return (new { error = "pipeline_not_ready", errorClass = "unavailable" },
                "Cabinet pipeline is not ready");

        byte[] bodyBytes = [];
        if (body is { ValueKind: JsonValueKind.Object or JsonValueKind.Array } el)
            bodyBytes = JsonSerializer.SerializeToUtf8Bytes(el);

        var queryString = BuildQuery(query);
        using var scope = _scopes.CreateScope();
        var inner = new DefaultHttpContext();
        inner.RequestServices = scope.ServiceProvider;
        inner.Items[NestedItemKey] = true;
        if (int.TryParse(_http.HttpContext?.User.FindFirst("apiKeyId")?.Value, out var originApiKeyId))
            inner.Items[OriginApiKeyIdItemKey] = originApiKeyId;
        inner.Request.Method = method;
        inner.Request.Scheme = "http";
        inner.Request.Host = _http.HttpContext?.Request.Host.HasValue == true
            ? _http.HttpContext.Request.Host
            : new HostString("localhost");
        inner.Request.Path = apiPath;
        inner.Request.QueryString = queryString;
        inner.Request.Headers["X-CSRF"] = "1";
        inner.Request.Headers.Cookie = BuildCookieHeader(storeId, userId);
        inner.Request.ContentType = "application/json";
        inner.Request.Body = new MemoryStream(bodyBytes);
        inner.Request.ContentLength = bodyBytes.Length;
        inner.Features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            RemoteIpAddress = IPAddress.Parse("127.255.255.1"),
            ConnectionId = "agent-nested",
        });

        using var responseBuffer = new BoundedResponseStream(inner.Response, MaxResponseBytes);
        inner.Response.Body = responseBuffer;

        var previous = _http.HttpContext;
        _http.HttpContext = inner;
        using var abort = ct.Register(inner.Abort);
        try
        {
            await next(inner);
        }
        catch (BinaryResponseRejectedException)
        {
            return BinaryResponseNotSupported(inner.Response.StatusCode, inner.Response.ContentType);
        }
        catch (ResponseBodyTooLargeException)
        {
            return TextResponseTooLarge(inner.Response.StatusCode, responseBuffer.BytesObserved, inner.Response.ContentType);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Nested cabinet request {Method} {Path} failed", method, apiPath);
            return (new { error = "request_failed", errorClass = "upstream_error", message = ex.Message },
                ex.Message);
        }
        finally
        {
            _http.HttpContext = previous;
        }

        if (responseBuffer.IsBinaryResponse)
            return BinaryResponseNotSupported(inner.Response.StatusCode, inner.Response.ContentType);

        return InterpretResponse(inner.Response.StatusCode, responseBuffer.ToArray());
    }

    private static (object result, string summary) BinaryResponseNotSupported(int status, string? contentType) =>
        (new
        {
            error = "binary_response_requires_download",
            errorClass = "transport_mismatch",
            status,
            contentType,
            hint = "Do not transfer file bytes through platform_request or MCP JSON. Call create_file_download_link with the file id, then GET its short-lived downloadUrl directly; Range requests are supported.",
        }, "Binary response rejected before buffering; use create_file_download_link and download it over HTTP");

    private static (object result, string summary) TextResponseTooLarge(int status, long bytesObserved, string? contentType) =>
        (new
        {
            error = "response_too_large",
            errorClass = "limit",
            status,
            limitBytes = MaxResponseBytes,
            bytesAtLeast = bytesObserved,
            format = IsJsonContentType(contentType) ? "json" : "text",
            hint = "Keep JSON/text reads within 256,000 bytes by requesting a smaller page or narrower fields. For a file, call create_file_download_link and download it over HTTP.",
        }, $"HTTP {status} text response exceeded the {MaxResponseBytes}-byte limit");

    private static bool IsJsonContentType(string? contentType)
    {
        var mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType != null
            && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("text/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBinaryResponse(HttpResponse response)
    {
        if (response.Headers.ContentDisposition.ToString().Contains("attachment", StringComparison.OrdinalIgnoreCase))
            return true;

        var mediaType = response.ContentType?.Split(';', 2)[0].Trim();
        if (string.IsNullOrEmpty(mediaType) || IsJsonContentType(mediaType))
            return false;

        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/x-ndjson", StringComparison.OrdinalIgnoreCase))
            return false;

        return mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("model/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("font/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BoundedResponseStream(HttpResponse response, int maxBytes) : Stream
    {
        private readonly MemoryStream _buffer = new(Math.Min(maxBytes, 16_384));

        public long BytesObserved { get; private set; }
        public bool IsBinaryResponse => CabinetRequestInvoker.IsBinaryResponse(response);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _buffer.Length;
        public override long Position
        {
            get => _buffer.Length;
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => _buffer.ToArray();
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCanCapture(buffer.Length);
            _buffer.Write(buffer);
            BytesObserved += buffer.Length;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCanCapture(buffer.Length);
            _buffer.Write(buffer.Span);
            BytesObserved += buffer.Length;
            return ValueTask.CompletedTask;
        }

        private void EnsureCanCapture(int incomingBytes)
        {
            if (IsBinaryResponse)
                throw new BinaryResponseRejectedException();
            if (_buffer.Length + incomingBytes > maxBytes)
            {
                BytesObserved = Math.Max(BytesObserved, (long)maxBytes + 1);
                throw new ResponseBodyTooLargeException();
            }
        }
    }

    private sealed class BinaryResponseRejectedException : Exception { }
    private sealed class ResponseBodyTooLargeException : Exception { }

    internal static (object result, string summary) InterpretResponse(int status, byte[] bytes)
    {
        if (status >= 400)
            return FailHttp(status, bytes);

        var working = bytes;
        var listNote = "";
        var capped = TryCapLists(bytes);
        if (capped != null)
        {
            working = capped;
            listNote = " (trimmed list)";
        }

        if (working.Length > MaxResponseBytes)
        {
            var slim = TrySlimJson(working);
            if (slim != null && slim.Length <= MaxResponseBytes)
                return (ParsePayload(slim, status), $"HTTP {status} (trimmed heavy fields)");
            return (new
            {
                error = "response_too_large",
                errorClass = "limit",
                status,
                bytes = working.Length,
                hint = "Do not GET a full product card. Use category attributes or sql_readonly on one table.",
            }, $"HTTP {status} too large ({working.Length} bytes)");
        }

        var parsed = ParsePayload(working, status);
        var summary = IsQueuedJob(working)
            ? $"HTTP {status} queued"
            : $"HTTP {status}";
        return (parsed, summary + listNote);
    }

    private static (object, string) FailHttp(int status, byte[] bytes)
    {
        string? code = null;
        string? title = null;
        object? data = null;
        object? body = null;
        var validationErrorsFlattened = false;
        if (bytes.Length > 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    code = Str(root, "code");
                    title = Str(root, "title") ?? Str(root, "message");
                    if (root.TryGetProperty("data", out var d))
                        data = JsonSerializer.Deserialize<object>(d.GetRawText(), JsonRead);
                    var details = FlattenValidation(root);
                    validationErrorsFlattened = !string.IsNullOrEmpty(details);
                    if (validationErrorsFlattened)
                        title = string.IsNullOrEmpty(title) || IsGenericValidationTitle(title)
                            ? details
                            : title + " " + details;
                }
                body = JsonSerializer.Deserialize<object>(root.GetRawText(), JsonRead);
            }
            catch (JsonException)
            {
                title = Encoding.UTF8.GetString(bytes);
            }
        }

        if (status == 404)
        {
            // A 404 that survived gateway preflight means the route exists but the
            // resource does not — it is never an instruction to stop inventing routes.
            code = "not_found";
            title ??= "Not found";
        }
        code ??= "http_error";
        title ??= $"Request failed ({status})";
        var atCap = string.Equals(code, "product_limit_reached", StringComparison.OrdinalIgnoreCase);
        var summary = atCap
            ? "Product limit reached — nothing was created."
            : title;
        return (new
        {
            error = code,
            errorClass = ErrorClass(status, validationErrorsFlattened),
            status,
            title,
            hint = (string?)null,
            data,
            body,
        }, summary);
    }

    private static string ErrorClass(int status, bool validationErrorsFlattened) =>
        status switch
        {
            400 => validationErrorsFlattened ? "validation_failed" : "bad_request",
            401 or 403 => "forbidden",
            404 => "resource_not_found",
            409 => "conflict",
            413 => "limit",
            422 => "validation_failed",
            429 => "quota",
            >= 500 => "upstream_error",
            _ => "bad_request",
        };

    private static bool IsGenericValidationTitle(string title) =>
        title.Contains("One or more validation errors", StringComparison.OrdinalIgnoreCase);

    private static string? FlattenValidation(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Object)
            return null;
        var msgs = new List<string>();
        foreach (var prop in errors.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in prop.Value.EnumerateArray())
            {
                var raw = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                msgs.Add(SoftenValidationMessage(raw));
            }
        }
        if (msgs.Count == 0) return null;
        return string.Join(" ", msgs.Distinct(StringComparer.Ordinal));
    }

    private static string SoftenValidationMessage(string message)
    {
        if (message.Contains("could not be converted to System.String", StringComparison.OrdinalIgnoreCase))
            return "Characteristic values must be text (quote numbers; several values as a JSON text array).";
        var pathAt = message.IndexOf(" Path:", StringComparison.Ordinal);
        if (pathAt > 0)
            message = message[..pathAt].Trim();
        return message;
    }

    private static object ParsePayload(byte[] bytes, int status)
    {
        if (bytes.Length == 0)
            return new { status };
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText(), JsonRead)
                ?? new { status };
        }
        catch (JsonException)
        {
            return new { status, text = Encoding.UTF8.GetString(bytes) };
        }
    }

    internal static byte[]? TryCapLists(byte[] bytes)
    {
        try
        {
            var node = JsonNode.Parse(Encoding.UTF8.GetString(bytes));
            if (node is not JsonObject obj)
                return null;
            if (obj["items"] is not JsonArray items || items.Count <= MaxListItems)
                return null;
            var removed = items.Count - MaxListItems;
            while (items.Count > MaxListItems)
                items.RemoveAt(items.Count - 1);
            obj["itemsTruncated"] = removed;
            // The returned prefix is not the last page, even when the upstream
            // page was terminal. Offset clients advance by the received count.
            if (obj["lastPage"] is JsonValue lastPage && lastPage.TryGetValue<bool>(out _))
                obj["lastPage"] = false;
            return Encoding.UTF8.GetBytes(node.ToJsonString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsQueuedJob(byte[] bytes)
    {
        if (bytes.Length == 0)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (!doc.RootElement.TryGetProperty("status", out var st)
                || st.ValueKind != JsonValueKind.String)
                return false;
            var s = st.GetString();
            return s is not null
                && (s.Equals("pending", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("running", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("queued", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static byte[]? TrySlimJson(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var node = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText());
            StripHeavy(node);
            return node == null ? null : Encoding.UTF8.GetBytes(node.ToJsonString());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void StripHeavy(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            obj.Remove("files");
            obj.Remove("images");
            obj.Remove("notes");
            obj.Remove("store");
            if (obj["category"] is System.Text.Json.Nodes.JsonObject cat)
                cat.Remove("attributes");
            foreach (var prop in obj)
                StripHeavy(prop.Value);
        }
        else if (node is System.Text.Json.Nodes.JsonArray arr)
        {
            foreach (var item in arr)
                StripHeavy(item);
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private string BuildCookieHeader(int storeId, int userId)
    {
        var outer = _http.HttpContext;
        string jwt;
        if (outer?.Request.Cookies.TryGetValue("access_token", out var cookieJwt) == true
            && !string.IsNullOrWhiteSpace(cookieJwt))
        {
            jwt = cookieJwt;
        }
        else
        {
            jwt = _jwt.GenerateToken(userId, "agent");
        }

        return $"selected_store_id={storeId}; access_token={jwt}";
    }

    private static QueryString BuildQuery(IReadOnlyDictionary<string, string> query)
    {
        if (query.Count == 0) return QueryString.Empty;
        return QueryString.Create(query.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
    }
}
