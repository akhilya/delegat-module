using System.Net;
using System.Text.Json;

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
            await HandleApiExceptionAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
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
