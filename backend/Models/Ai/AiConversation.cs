using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Ai;

public class AiConversation
{
    [Key] public int Id { get; set; }

    /// <summary>crypto.randomUUID() from frontend, stable for one chat session.</summary>
    [StringLength(36)] public string sessionId { get; set; } = null!;

    public int storeId { get; set; }
    public int userId { get; set; }

    [StringLength(200)] public string? title { get; set; }

    /// <summary>Model identifier used in this conversation, e.g. "claude-sonnet-4-6".</summary>
    [StringLength(200)] public string? modelName { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime lastMessageAt { get; set; } = DateTime.UtcNow;
    public int messageCount { get; set; }

    public ICollection<AiConversationMessage> messages { get; set; } = [];
}
