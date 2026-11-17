using System.ComponentModel.DataAnnotations;

namespace Delegat.Models.Ai;

/// <summary>
/// One immutable file captured from a sandbox run. Bytes live privately in object
/// storage under <c>__ai-generated/</c>; the model only ever sees the metadata.
/// </summary>
public class AiGeneratedArtifact
{
    /// <summary>Opaque 32-hex artifact id; also the storage-key segment.</summary>
    [Key]
    [StringLength(32)]
    public string Id { get; set; } = "";

    public int storeId { get; set; }
    public int userId { get; set; }

    /// <summary>The audited sandbox run that produced this artifact.</summary>
    public int sandboxRunId { get; set; }

    [StringLength(180)]
    public string fileName { get; set; } = "";

    [StringLength(200)]
    public string contentType { get; set; } = "application/octet-stream";

    public long length { get; set; }

    [StringLength(64)]
    public string sha256 { get; set; } = "";

    [StringLength(500)]
    public string storageKey { get; set; } = "";

    public DateTime createdAtUtc { get; set; }
    public DateTime expiresAtUtc { get; set; }

    /// <summary>Server-only lifecycle: capturing | ready. Never client-writable.</summary>
    [StringLength(16)]
    public string state { get; set; } = "";
}
