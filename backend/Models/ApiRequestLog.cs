using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models;

/// <summary>
/// Inbound Public API (/api/v1) request log for API key activity.
/// </summary>
public class ApiRequestLog : IEntity
{
    [Key]
    public int Id { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public int? apiKeyId { get; set; }

    public int? userId { get; set; }

    [StringLength(32)]
    public string? keyDisplayHint { get; set; }

    [StringLength(16)]
    public string method { get; set; } = "";

    [StringLength(1000)]
    public string path { get; set; } = "";

    [StringLength(2000)]
    public string? queryString { get; set; }

    public int statusCode { get; set; }

    public int durationMs { get; set; }

    [StringLength(64)]
    public string? remoteIpAddress { get; set; }

    [StringLength(512)]
    public string? userAgent { get; set; }

    [Column(TypeName = "longtext")]
    public string? requestBody { get; set; }

    [Column(TypeName = "longtext")]
    public string? responseBody { get; set; }

    [StringLength(2000)]
    public string? errorMessage { get; set; }
}
