using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Delegat.Services;

namespace Delegat.Infrastructure.Middleware;

/// <summary>
/// Logs inbound Public API (/api/v1) requests for the API keys activity page.
/// </summary>
public sealed class PublicApiRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public PublicApiRequestLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IApiRequestLogger logger,
        ILoggingSettingsService loggingSettings,
        IPublicApiSettingsService publicApiSettings)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1"))
        {
            await _next(context);
            return;
        }

        if (!loggingSettings.IsApiRequestLoggingEnabled
            || !publicApiSettings.IsPublicApiEnabled)
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        string? requestBody = null;
        string? responseBody = null;

        context.Request.EnableBuffering();
        try
        {
            if (context.Request.ContentLength is null or > 0)
            {
                requestBody = await ReadLimitedAsync(context.Request.Body, ApiRequestLogService.MaxStoredBodyChars);
                context.Request.Body.Position = 0;
            }
        }
        catch
        {
            requestBody = null;
        }

        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            try
            {
                responseBuffer.Position = 0;
                if (responseBuffer.Length > 0)
                {
                    responseBody = await ReadLimitedAsync(responseBuffer, ApiRequestLogService.MaxStoredBodyChars);
                }
                responseBuffer.Position = 0;
                await responseBuffer.CopyToAsync(originalBody);
            }
            catch
            {
                // ignore copy failures
            }
            finally
            {
                context.Response.Body = originalBody;
            }

            try
            {
                int? apiKeyId = null;
                int? userId = null;
                string? keyHint = null;

                var user = context.User;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    var keyClaim = user.FindFirst("apiKeyId")?.Value;
                    if (int.TryParse(keyClaim, out var kid))
                        apiKeyId = kid;
                    var userClaim = user.FindFirst("userId")?.Value
                        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (int.TryParse(userClaim, out var uid))
                        userId = uid;
                    keyHint = user.FindFirst("apiKeyHint")?.Value;
                }

                logger.Log(new ApiRequestLogEntry
                {
                    CreatedAt = DateTime.UtcNow,
                    ApiKeyId = apiKeyId,
                    UserId = userId,
                    KeyDisplayHint = keyHint,
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value ?? "",
                    QueryString = context.Request.QueryString.HasValue
                        ? context.Request.QueryString.Value
                        : null,
                    StatusCode = context.Response.StatusCode,
                    DurationMs = (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue),
                    RemoteIpAddress = context.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context.Request.Headers.UserAgent.ToString(),
                    RequestBody = requestBody,
                    ResponseBody = responseBody,
                    ErrorMessage = context.Response.StatusCode >= 400
                        ? TruncateError(responseBody)
                        : null,
                });
            }
            catch
            {
                // never break the request on logging
            }
        }
    }

    private static async Task<string?> ReadLimitedAsync(Stream stream, int maxChars)
    {
        // Approximate: read up to maxChars UTF-8 bytes (+ small slack), then truncate by char count.
        var maxBytes = Math.Min(checked(maxChars * 4 + 16), int.MaxValue);
        await using var limited = new LimitedReadStream(stream, maxBytes);
        using var reader = new StreamReader(
            limited,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrEmpty(text)) return text;
        if (text.Length <= maxChars) return text;
        return text[..maxChars] + ApiRequestLogService.TruncationMarker;
    }

    private static string? TruncateError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        return body.Length <= 500 ? body : body[..500];
    }

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public LimitedReadStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _remaining = maxBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var toRead = (int)Math.Min(count, _remaining);
            var read = _inner.Read(buffer, offset, toRead);
            _remaining -= read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_remaining <= 0) return 0;
            var toRead = (int)Math.Min(count, _remaining);
            var read = await _inner.ReadAsync(buffer.AsMemory(offset, toRead), cancellationToken);
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0) return 0;
            if (buffer.Length > _remaining)
                buffer = buffer[..(int)_remaining];
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            _remaining -= read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
