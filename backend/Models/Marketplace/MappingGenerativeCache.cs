using System.ComponentModel.DataAnnotations;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models.Marketplace;

/// <summary>
/// Cached LLM output for an llmGenerate graph node. Keyed by mapping, node, model, prompt and input.
/// </summary>
public class MappingGenerativeCache : IEntity
{
    [Key]
    public int Id { get; set; }

    public int mappingId { get; set; }

    [Required, StringLength(128)]
    public string nodeId { get; set; } = null!;

    public int? productId { get; set; }

    [Required, StringLength(64)]
    public string promptHash { get; set; } = null!;

    public int? seed { get; set; }

    public int modelConfigId { get; set; }

    [Required, StringLength(64)]
    public string inputHash { get; set; } = null!;

    public string? output { get; set; }

    /// <summary>pending | ready | failed</summary>
    [Required, StringLength(16)]
    public string status { get; set; } = "pending";

    [StringLength(64)]
    public string? operationId { get; set; }

    public string? errorMessage { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public DateTime? completedAt { get; set; }
}