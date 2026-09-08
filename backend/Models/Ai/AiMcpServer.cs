using System.ComponentModel.DataAnnotations;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models.Ai;

public class AiMcpServer : IEntity
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string name { get; set; } = null!;

    [Required, StringLength(500)]
    public string url { get; set; } = null!;

    public string? apiKeyEncrypted { get; set; }

    public bool isEnabled { get; set; } = true;

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}
