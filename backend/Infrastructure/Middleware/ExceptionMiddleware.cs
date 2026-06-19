using System.Net;
using System.Text.Json;
using PrintO.Services;

namespace PrintO.Infrastructure.Middleware;

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

        var response = new ErrorResponse
        {
            Title = exception.Message,
            Status = exception.StatusCode,
            Errors = exception.Errors,
            Data = exception.Data
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new ErrorResponse
        {
            Title = _environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred.",
            Status = (int)HttpStatusCode.InternalServerError
        };

        // Include stack trace in development
        if (_environment.IsDevelopment())
        {
            response.Detail = exception.StackTrace;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
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
