using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Singleton row (Id = 1) for instance logging toggles. Seeded from env on first boot;
/// afterwards the admin UI is the source of truth (no restart required).
/// </summary>
public class InstanceLoggingSettings
{
    public const int SingletonId = 1;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = SingletonId;

    public bool logErrorsEnabled { get; set; } = true;
    public bool logApiRequestsEnabled { get; set; } = false;
    public bool logAdapterHttpEnabled { get; set; } = false;

    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
}
