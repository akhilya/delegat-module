namespace PrintO.Infrastructure.Middleware;

/// <summary>
/// Custom exception for API errors with HTTP status codes
/// Replaces Zorro QueryException
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public Dictionary<string, string[]>? Errors { get; }
    public new object? Data { get; }

    public ApiException(
        string message = "An error occurred while processing your request.",
        int statusCode = StatusCodes.Status400BadRequest,
        Dictionary<string, string[]>? errors = null,
        object? data = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
        Data = data;
    }

    /// <summary>
    /// Create a BadRequest exception
    /// </summary>
    public static ApiException BadRequest(string message = "Bad request")
        => new(message, StatusCodes.Status400BadRequest);

    /// <summary>
    /// Create a BadRequest exception with structured details.
    /// </summary>
    public static ApiException BadRequest(string message, object? data)
        => new(message, StatusCodes.Status400BadRequest, data: data);

    /// <summary>
    /// Create a BadRequest exception with multiple error messages
    /// </summary>
    public static ApiException BadRequest(string[] messages)
        => new(string.Join("; ", messages), StatusCodes.Status400BadRequest);

    /// <summary>
    /// Create an Unauthorized exception
    /// </summary>
    public static ApiException Unauthorized(string message = "Unauthorized")
        => new(message, StatusCodes.Status401Unauthorized);

    /// <summary>
    /// Create a Forbidden exception
    /// </summary>
    public static ApiException Forbidden(string message = "Forbidden")
        => new(message, StatusCodes.Status403Forbidden);

    /// <summary>
    /// Create a NotFound exception
    /// </summary>
    public static ApiException NotFound(string message = "Resource not found")
        => new(message, StatusCodes.Status404NotFound);

    /// <summary>
    /// Create a Conflict exception
    /// </summary>
    public static ApiException Conflict(string message = "Conflict")
        => new(message, StatusCodes.Status409Conflict);

    /// <summary>
    /// Create an InternalServerError exception
    /// </summary>
    public static ApiException InternalServerError(string message = "Internal server error")
        => new(message, StatusCodes.Status500InternalServerError);
}
