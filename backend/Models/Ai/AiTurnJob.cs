using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Ai;

/// <summary>A durable cabinet turn; leases fence persistence, not external side effects.</summary>
public sealed class AiTurnJob
{
    [Key] public long Id { get; set; }
    [StringLength(32)] public string runId { get; set; } = "";
    public int conversationId { get; set; }
    public AiConversation conversation { get; set; } = null!;
    [StringLength(36)] public string sessionId { get; set; } = "";
    // NULL after completion: the unique index permits only one active job per session.
    [StringLength(36)] public string? activeSessionId { get; set; }
    public int storeId { get; set; }
    public int userId { get; set; }
    public int payloadVersion { get; set; } = 1;
    [StringLength(16)] public string kind { get; set; } = "chat";
    [StringLength(16)] public string engine { get; set; } = "legacy";
    [StringLength(20)] public string status { get; set; } = "pending";
    [StringLength(16)] public string phase { get; set; } = "chat";
    [Column(TypeName = "longtext")] public string requestJson { get; set; } = "{}";
    [Column(TypeName = "longtext")] public string checkpointsJson { get; set; } = "[]";
    [Column(TypeName = "longtext")] public string? bindingJson { get; set; }
    public int? startedStep { get; set; }
    public bool executionEnded { get; set; }
    public bool cancelRequested { get; set; }
    [StringLength(32)] public string? lastEventType { get; set; }
    public long lastSequence { get; set; }
    public long revision { get; set; }
    [StringLength(32)] public string? leaseOwner { get; set; }
    public DateTime? leaseExpiresAtUtc { get; set; }
    public int attempts { get; set; }
    public DateTime createdAtUtc { get; set; }
    public DateTime updatedAtUtc { get; set; }
}

public sealed class AiTurnJournalEvent
{
    public long jobId { get; set; }
    public AiTurnJob job { get; set; } = null!;
    public long sequence { get; set; }
    [Column(TypeName = "longtext")] public string eventJson { get; set; } = "";
}
