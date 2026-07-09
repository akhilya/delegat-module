using System.ComponentModel.DataAnnotations;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models.Ai;

public class AiModelConfig : IEntity
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string name { get; set; } = null!;

    [Required, StringLength(500)]
    public string apiUrl { get; set; } = null!;

    [Required]
    public string apiKeyEncrypted { get; set; } = null!;

    [Required, StringLength(200)]
    public string modelId { get; set; } = null!;

    public bool isDefault { get; set; }

    public bool isEnabled { get; set; } = true;

    public AiModelProvider provider { get; set; } = AiModelProvider.Anthropic;

    public int maxTokens { get; set; } = 8192;

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? proxyUrl { get; set; }
}

public enum AiModelProvider
{
    Anthropic,
    OpenAiCompatible
}
