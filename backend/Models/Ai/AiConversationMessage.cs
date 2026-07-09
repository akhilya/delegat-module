using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Ai;

public class AiConversationMessage
{
    [Key] public int Id { get; set; }

    [ForeignKey(nameof(conversation))]
    public int conversationId { get; set; }
    public AiConversation conversation { get; set; } = null!;

    /// <summary>"user" or "assistant"</summary>
    [StringLength(20)] public string role { get; set; } = "";

    [Column(TypeName = "longtext")] public string content { get; set; } = "";

    /// <summary>JSON array of {name, input, summary} for tool calls made in this turn.</summary>
    [Column(TypeName = "longtext")] public string? toolCallsJson { get; set; }

    /// <summary>+1 (👍) or -1 (👎), null if not rated.</summary>
    public int? feedbackRating { get; set; }

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}
