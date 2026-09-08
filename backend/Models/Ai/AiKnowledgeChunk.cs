using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Ai;

public class AiKnowledgeChunk
{
    [Key] public int Id { get; set; }

    [StringLength(400)] public string sourcePath { get; set; } = "";
    [StringLength(200)] public string title { get; set; } = "";

    [Column(TypeName = "longtext")]
    public string body { get; set; } = "";

    public int? storeId { get; set; }
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}

public class AiSandboxRun
{
    [Key] public int Id { get; set; }
    public int userId { get; set; }
    public int storeId { get; set; }
    [StringLength(64)] public string scriptHash { get; set; } = "";
    public int? exitCode { get; set; }
    public int durationMs { get; set; }
    [StringLength(4000)] public string? stdoutPreview { get; set; }
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}

public class AiAgentAudit
{
    [Key] public int Id { get; set; }
    public int userId { get; set; }
    public int storeId { get; set; }
    [StringLength(64)] public string tool { get; set; } = "";
    [StringLength(500)] public string summary { get; set; } = "";
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}
