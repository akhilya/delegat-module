using System.ComponentModel.DataAnnotations;

namespace Delegat.Models;

/// <summary>
/// Tracks versions of platform and plugins for data migration and field sync purposes.
/// </summary>
public class SystemVersion
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Version key: "platform", "plugin:wildberries", "plugin:ozon", etc.
    /// </summary>
    [StringLength(KEY_MAX_LENGTH)]
    public string key { get; set; } = null!;

    /// <summary>
    /// Semantic version string (e.g. "1.5.5").
    /// </summary>
    [StringLength(VERSION_MAX_LENGTH)]
    public string version { get; set; } = null!;

    /// <summary>
    /// When this version was recorded.
    /// </summary>
    public DateTime appliedAt { get; set; } = DateTime.UtcNow;

    public const int KEY_MAX_LENGTH = 100;
    public const int VERSION_MAX_LENGTH = 50;
}
