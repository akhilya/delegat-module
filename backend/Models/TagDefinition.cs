using PrintO.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

/// <summary>
/// Store-level tag definition. Shared across all files in the store.
/// Editing a TagDefinition changes the tag everywhere it's used.
/// </summary>
public class TagDefinition
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Tag label text.
    /// </summary>
    [StringLength(TAG_NAME_MAX_LENGTH)]
    public string name { get; set; } = null!;

    /// <summary>
    /// Display color.
    /// </summary>
    public FileTagColor color { get; set; }

    /// <summary>
    /// Store this tag belongs to.
    /// </summary>
    [ForeignKey(nameof(store))]
    public int storeId { get; set; }
    public Store store { get; set; } = null!;

    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    public const int TAG_NAME_MAX_LENGTH = 50;
}
