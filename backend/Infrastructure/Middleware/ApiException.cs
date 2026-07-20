namespace Delegat.Infrastructure.Middleware;

/// <summary>
/// Custom exception for API errors with HTTP status codes and stable error codes.
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public string? Code { get; }
    public Dictionary<string, string[]>? Errors { get; }
    public new object? Data { get; }

    public ApiException(
        string message = "An error occurred while processing your request.",
        int statusCode = StatusCodes.Status400BadRequest,
        Dictionary<string, string[]>? errors = null,
        object? data = null,
        Exception? innerException = null,
        string? code = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors;
        Data = data;
        Code = code;
    }

    public static ApiException BadRequest(string message = "Bad request", string? code = "bad_request")
        => new(message, StatusCodes.Status400BadRequest, code: code);

    public static ApiException BadRequest(string message, object? data, string? code = "bad_request")
        => new(message, StatusCodes.Status400BadRequest, data: data, code: code);

    public static ApiException BadRequest(string message, object? data, Exception? innerException, string? code = "bad_request")
        => new(message, StatusCodes.Status400BadRequest, data: data, innerException: innerException, code: code);

    public static ApiException BadRequest(string[] messages, string? code = "bad_request")
        => new(string.Join("; ", messages), StatusCodes.Status400BadRequest, code: code);

    public static ApiException Unauthorized(string message = "Unauthorized", string? code = "unauthorized")
        => new(message, StatusCodes.Status401Unauthorized, code: code);

    public static ApiException Forbidden(string message = "Forbidden", string? code = "forbidden")
        => new(message, StatusCodes.Status403Forbidden, code: code);

    public static ApiException NotFound(string message = "Resource not found", string? code = "not_found")
        => new(message, StatusCodes.Status404NotFound, code: code);

    public static ApiException Conflict(string message = "Conflict", string? code = "conflict")
        => new(message, StatusCodes.Status409Conflict, code: code);

    public static ApiException InternalServerError(string message = "Internal server error", string? code = "internal_error")
        => new(message, StatusCodes.Status500InternalServerError, code: code);
}
