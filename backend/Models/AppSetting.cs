using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Delegat.Models;

public class AppSetting : IEntity
{
    public int Id { get; set; }

    [Required]
    [StringLength(KEY_MAX_LENGTH)]
    public string key { get; set; } = "";

    [Required]
    [StringLength(LABEL_MAX_LENGTH)]
    public string label { get; set; } = "";

    [StringLength(VALUE_MAX_LENGTH)]
    public string? value { get; set; }

    [Required]
    [StringLength(TYPE_MAX_LENGTH)]
    public string valueType { get; set; } = "integer";

    [StringLength(DESCRIPTION_MAX_LENGTH)]
    public string? description { get; set; }

    public bool isEditable { get; set; } = true;

    public DateTime updatedAt { get; set; } = DateTime.UtcNow;

    public int? updatedByUserId { get; set; }

    public User? updatedByUser { get; set; }

    public const int KEY_MAX_LENGTH = 100;
    public const int LABEL_MAX_LENGTH = 200;
    public const int VALUE_MAX_LENGTH = 1000;
    public const int TYPE_MAX_LENGTH = 32;
    public const int DESCRIPTION_MAX_LENGTH = 1000;
}
