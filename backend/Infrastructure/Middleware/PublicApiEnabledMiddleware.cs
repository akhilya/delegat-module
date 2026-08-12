using System.Text.Json;
using Delegat.Infrastructure.Configuration;

namespace Delegat.Infrastructure.Middleware;

/// <summary>
/// Short-circuits Public API (/api/v1) and Swagger when the plan flag or instance toggle is off.
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
        var planAllows = FeatureFlags.IsApiUsageEnabled();
        if (planAllows && publicApiSettings.IsPublicApiEnabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;
        if (path.StartsWithSegments("/api/v1") || path.StartsWithSegments("/swagger"))
        {
            var (code, message) = planAllows
                ? ("PUBLIC_API_DISABLED", "Public API is disabled for this instance.")
                : ("API_USAGE_DISABLED", "Public API is not included in this plan.");

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code,
                    message,
                    details = (object?)null,
                }
            }, JsonOptions));
            return;
        }

        await _next(context);
    }
}
