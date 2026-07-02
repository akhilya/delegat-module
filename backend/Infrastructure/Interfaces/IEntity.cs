using System.ComponentModel.DataAnnotations;

namespace Delegat.Infrastructure.Interfaces;

/// <summary>
/// Base interface for all entities with Id property
/// Replaces Zorro.Data.Interfaces.IEntity
/// </summary>
public interface IEntity
{
    [Key]
    int Id { get; set; }
}
