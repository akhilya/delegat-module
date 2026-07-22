using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Singleton row (Id = 1) for Public API enablement. Seeded from env on first boot;
/// afterwards the admin UI is the source of truth (no restart required).
/// </summary>
public class InstancePublicApiSettings
{
    public const int SingletonId = 1;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = SingletonId;

    public bool publicApiEnabled { get; set; } = true;

    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
}
