using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models.Marketplace;

/// <summary>
/// Persisted outbound HTTP call from a marketplace adapter to an external API.
/// </summary>
public class AdapterHttpLog : IEntity
{
    [Key]
    public int Id { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public int storeId { get; set; }

    public int connectionId { get; set; }

    [StringLength(64)]
    public string adapterKey { get; set; } = "";

    [StringLength(16)]
    public string method { get; set; } = "";

    [StringLength(2000)]
    public string url { get; set; } = "";

    [StringLength(1000)]
    public string path { get; set; } = "";

    public int statusCode { get; set; }

    public int durationMs { get; set; }

    [Column(TypeName = "longtext")]
    public string? requestHeadersJson { get; set; }

    [Column(TypeName = "longtext")]
    public string? requestBody { get; set; }

    [Column(TypeName = "longtext")]
    public string? responseHeadersJson { get; set; }

    [Column(TypeName = "longtext")]
    public string? responseBody { get; set; }

    [StringLength(2000)]
    public string? errorMessage { get; set; }

    [StringLength(64)]
    public string? operationId { get; set; }
}
