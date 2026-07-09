using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models.Ai;

public class AiOperation : IEntity
{
    [Key]
    public int Id { get; set; }

    public int storeId { get; set; }

    public int userId { get; set; }

    [Required, StringLength(100)]
    public string operationType { get; set; } = null!;

    [Required, StringLength(500)]
    public string description { get; set; } = null!;

    public string rollbackDataJson { get; set; } = "{}";

    public bool canRollback { get; set; } = true;

    public AiOperationStatus status { get; set; } = AiOperationStatus.Completed;

    public DateTime createdAt { get; set; } = DateTime.UtcNow;
}

public enum AiOperationStatus
{
    Completed,
    RolledBack
}
