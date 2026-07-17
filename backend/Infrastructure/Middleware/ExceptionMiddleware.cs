using System.Net;
using System.Text.Json;
using Delegat.Services;

namespace Delegat.Infrastructure.Middleware;

/// <summary>
/// Middleware for global exception handling
/// Replaces Zorro.Middlewares.ErrorHandlingMiddleware
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException ex)
        {
            if (ex.StatusCode >= (int)HttpStatusCode.InternalServerError || ex.Data != null)
                await PersistErrorLogAsync(context, ex, ex.StatusCode);

            await HandleApiExceptionAsync(context, ex);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Request was canceled by client: {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await PersistErrorLogAsync(context, ex, (int)HttpStatusCode.InternalServerError);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task PersistErrorLogAsync(HttpContext context, Exception exception, int statusCode)
    {
        var errorLogService = context.RequestServices.GetService<IErrorLogService>();
        if (errorLogService == null) return;

        await errorLogService.LogAsync(context, exception, statusCode);
    }

    private Task HandleApiExceptionAsync(HttpContext context, ApiException exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = exception.StatusCode;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        if (IsPublicApi(context))
        {
            var code = exception.StatusCode switch
            {
                401 => "unauthorized",
                403 => "forbidden",
                404 => "not_found",
                409 => "conflict",
                422 => "validation_error",
                429 => "rate_limited",
                _ => "bad_request"
            };
            var publicBody = new
            {
                error = new
                {
                    code,
                    message = exception.Message,
                    details = exception.Data ?? (object?)exception.Errors
                }
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(publicBody, options));
        }

        var response = new ErrorResponse
        {
            Title = exception.Message,
            Status = exception.StatusCode,
            Errors = exception.Errors,
            Data = exception.Data
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        if (IsPublicApi(context))
        {
            var publicBody = new
            {
                error = new
                {
                    code = "internal_error",
                    message = _environment.IsDevelopment()
                        ? exception.Message
                        : "An unexpected error occurred."
                }
            };
            return context.Response.WriteAsync(JsonSerializer.Serialize(publicBody, options));
        }

        var response = new ErrorResponse
        {
            Title = _environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred.",
            Status = (int)HttpStatusCode.InternalServerError
        };

        if (_environment.IsDevelopment())
        {
            response.Detail = exception.StackTrace;
        }

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private static bool IsPublicApi(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api/v1");
}

/// <summary>
/// Error response model for API errors
/// </summary>
public class ErrorResponse
{
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? Detail { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
    public object? Data { get; set; }
}
