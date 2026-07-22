using System.Text.Json;

namespace Delegat.Infrastructure.Middleware;

/// <summary>
/// Short-circuits Public API (/api/v1) with 503 when the instance toggle is off.
/// </summary>
public sealed class PublicApiEnabledMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly RequestDelegate _next;

    public PublicApiEnabledMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, Services.IPublicApiSettingsService publicApiSettings)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1")
            && !publicApiSettings.IsPublicApiEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "PUBLIC_API_DISABLED",
                    message = "Public API is disabled for this instance.",
                    details = (object?)null,
                }
            }, JsonOptions));
            return;
        }

        await _next(context);
    }
}
