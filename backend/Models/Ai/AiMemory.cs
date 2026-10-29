using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Ai;

/// <summary>A durable, user-controlled fact available to the assistant across conversations.</summary>
public class AiMemory
{
    [Key] public int Id { get; set; }
    public int storeId { get; set; }
    public int userId { get; set; }
    [StringLength(80)] public string category { get; set; } = "general";
    [StringLength(120)] public string key { get; set; } = "";
    [Column(TypeName = "text")] public string value { get; set; } = "";
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
}
