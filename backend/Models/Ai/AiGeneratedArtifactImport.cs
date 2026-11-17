using System.ComponentModel.DataAnnotations;

namespace Delegat.Models.Ai;

/// <summary>
/// Durable per-import receipt. The id is the SHA-256 of a canonical fixed-field
/// binding (actor, store, artifact id/hash/length/expiry, filename/type, destination);
/// an exact replay resolves the same receipt instead of writing twice.
/// </summary>
public class AiGeneratedArtifactImport
{
    /// <summary>64-hex binding hash; one fence per exact artifact+destination.</summary>
    [Key]
    [StringLength(64)]
    public string Id { get; set; } = "";

    [StringLength(32)]
    public string artifactId { get; set; } = "";

    public int storeId { get; set; }
    public int userId { get; set; }

    /// <summary>store | product.</summary>
    [StringLength(16)]
    public string destination { get; set; } = "";

    public int? productId { get; set; }
    public int? folderId { get; set; }

    public long length { get; set; }

    /// <summary>Object key the destination file was/is being written to.</summary>
    [StringLength(500)]
    public string destinationKey { get; set; } = "";

    public DateTime createdAtUtc { get; set; }

    /// <summary>started | completed. A started row means in-progress or unknown.</summary>
    [StringLength(16)]
    public string state { get; set; } = "";

    /// <summary>The StoreFile or product File id once the import completed.</summary>
    public int? fileId { get; set; }
}
