using System.ComponentModel.DataAnnotations;

namespace Delegat.Models.Ai;

/// <summary>
/// Staged cabinet-chat file. Bytes live in MinIO under <c>__ai-chat-attach/</c>, not on a product.
/// </summary>
public class AiChatAttachment
{
    [Key] public int Id { get; set; }

    [StringLength(64)]
    public string sessionId { get; set; } = "";

    public int storeId { get; set; }
    public int userId { get; set; }

    [StringLength(500)]
    public string relativePath { get; set; } = "";

    [StringLength(255)]
    public string fileName { get; set; } = "";

    [StringLength(200)]
    public string contentType { get; set; } = "application/octet-stream";

    public long length { get; set; }

    [StringLength(500)]
    public string storageKey { get; set; } = "";

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}
