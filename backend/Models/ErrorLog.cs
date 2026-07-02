using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models;

public class ErrorLog : IEntity, IMapToDTO<ErrorLog.ErrorLogDTO>
{
    public int Id { get; set; }
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public int statusCode { get; set; }
    public string method { get; set; } = "";
    public string path { get; set; } = "";
    public string? queryString { get; set; }
    public string exceptionType { get; set; } = "";
    public string message { get; set; } = "";
    public string? stackTrace { get; set; }
    public string traceIdentifier { get; set; } = "";
    public int? userId { get; set; }
    public string? userName { get; set; }
    public string? remoteIpAddress { get; set; }

    public ErrorLogDTO MapToDTO(IServiceProvider serviceProvider)
    {
        return new ErrorLogDTO
        {
            id = Id,
            createdAt = createdAt,
            statusCode = statusCode,
            method = method,
            path = path,
            queryString = queryString,
            exceptionType = exceptionType,
            message = message,
            stackTrace = stackTrace,
            traceIdentifier = traceIdentifier,
            userId = userId,
            userName = userName,
            remoteIpAddress = remoteIpAddress
        };
    }

    public struct ErrorLogDTO
    {
        public int id { get; set; }
        public DateTime createdAt { get; set; }
        public int statusCode { get; set; }
        public string method { get; set; }
        public string path { get; set; }
        public string? queryString { get; set; }
        public string exceptionType { get; set; }
        public string message { get; set; }
        public string? stackTrace { get; set; }
        public string traceIdentifier { get; set; }
        public int? userId { get; set; }
        public string? userName { get; set; }
        public string? remoteIpAddress { get; set; }
    }
}
