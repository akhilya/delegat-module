using System.IdentityModel.Tokens.Jwt;
using Delegat.Models;

namespace Delegat.Services;

public interface IErrorLogService
{
    Task LogAsync(HttpContext context, Exception exception, int statusCode);
}

public class ErrorLogService : IErrorLogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ErrorLogService> _logger;
    private readonly ILoggingSettingsService _loggingSettings;

    public ErrorLogService(
        IServiceScopeFactory scopeFactory,
        ILogger<ErrorLogService> logger,
        ILoggingSettingsService loggingSettings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _loggingSettings = loggingSettings;
    }

    public async Task LogAsync(HttpContext context, Exception exception, int statusCode)
    {
        if (!_loggingSettings.IsErrorLoggingEnabled)
            return;

        try
        {
            // Use a fresh scope so a failed request DbContext (e.g. pending invalid Store)
            // cannot block persisting the error log.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DataContext>();

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

            db.errorLogs.Add(entry);
            await db.SaveChangesAsync();
        }
        catch (Exception logException)
        {
            _logger.LogWarning(logException, "Failed to persist error log");
        }
    }
}
