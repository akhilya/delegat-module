using System.IdentityModel.Tokens.Jwt;
using PrintO.Models;

namespace PrintO.Services;

public interface IErrorLogService
{
    Task LogAsync(HttpContext context, Exception exception, int statusCode);
}

public class ErrorLogService : IErrorLogService
{
    private readonly DataContext _context;
    private readonly ILogger<ErrorLogService> _logger;

    public ErrorLogService(DataContext context, ILogger<ErrorLogService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(HttpContext context, Exception exception, int statusCode)
    {
        try
        {
            var userIdValue = context.User.FindFirst("userId")?.Value;
            int? userId = int.TryParse(userIdValue, out var parsedUserId) ? parsedUserId : null;

            var entry = new ErrorLog
            {
                createdAt = DateTime.UtcNow,
                statusCode = statusCode,
                method = context.Request.Method,
                path = context.Request.Path.Value ?? "",
                queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                exceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                message = exception.Message,
                stackTrace = exception.ToString(),
                traceIdentifier = context.TraceIdentifier,
                userId = userId,
                userName = context.User.FindFirst(JwtRegisteredClaimNames.Name)?.Value,
                remoteIpAddress = context.Connection.RemoteIpAddress?.ToString()
            };

            _context.errorLogs.Add(entry);
            await _context.SaveChangesAsync();
        }
        catch (Exception logException)
        {
            _logger.LogWarning(logException, "Failed to persist error log");
        }
    }
}
