using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Plugin registry metadata synced from Control Plane (not the installed DLL).
/// </summary>
[Table("referencePlugins")]
public class ReferencePlugin
{
    [Key]
    [StringLength(KEY_MAX_LENGTH)]
    public string key { get; set; } = null!;

    [StringLength(DISPLAY_NAME_MAX_LENGTH)]
    public string displayName { get; set; } = null!;

    [StringLength(VERSION_MAX_LENGTH)]
    public string? latestVersion { get; set; }

    [StringLength(VERSION_MAX_LENGTH)]
    public string? minPlatformVersion { get; set; }

    [StringLength(ARTIFACT_URL_MAX_LENGTH)]
    public string? artifactUrl { get; set; }

    [Column(TypeName = "longtext")]
    public string? changelog { get; set; }

    public DateTime syncedAt { get; set; } = DateTime.UtcNow;

    public const int KEY_MAX_LENGTH = 64;
    public const int DISPLAY_NAME_MAX_LENGTH = 200;
    public const int VERSION_MAX_LENGTH = 64;
    public const int ARTIFACT_URL_MAX_LENGTH = 1000;
}
