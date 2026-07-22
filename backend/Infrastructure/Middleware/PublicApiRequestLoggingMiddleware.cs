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

    public async Task InvokeAsync(HttpContext context, IApiRequestLogger logger)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1"))
        {
            await _next(context);
            return;
        }

        if (!Delegat.Infrastructure.Configuration.FeatureFlags.IsApiRequestLoggingEnabled())
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
            if (context.Request.ContentLength is > 0 and <= ApiRequestLogService.MaxBodyChars)
            {
                using var reader = new StreamReader(
                    context.Request.Body,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                requestBody = await reader.ReadToEndAsync();
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
                if (responseBuffer.Length > 0 && responseBuffer.Length <= ApiRequestLogService.MaxBodyChars)
                {
                    using var reader = new StreamReader(responseBuffer, Encoding.UTF8, leaveOpen: true);
                    responseBody = await reader.ReadToEndAsync();
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

    private static string? TruncateError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        return body.Length <= 500 ? body : body[..500];
    }
}
