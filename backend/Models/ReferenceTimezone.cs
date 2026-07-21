using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Global IANA timezone catalog row synced from Control Plane.
/// </summary>
[Table("referenceTimezones")]
public class ReferenceTimezone
{
    /// <summary>IANA id, e.g. Europe/Moscow.</summary>
    [Key]
    [StringLength(ID_MAX_LENGTH)]
    public string id { get; set; } = null!;

    [StringLength(LABEL_MAX_LENGTH)]
    public string label { get; set; } = null!;

    public DateTime syncedAt { get; set; } = DateTime.UtcNow;

    public const int ID_MAX_LENGTH = 64;
    public const int LABEL_MAX_LENGTH = 200;
}
