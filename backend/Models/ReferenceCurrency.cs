using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Global currency catalog row synced from Control Plane Reference Catalog.
/// Store-specific rates live in <see cref="StoreCurrency"/>.
/// </summary>
[Table("referenceCurrencies")]
public class ReferenceCurrency
{
    [Key]
    [StringLength(CODE_MAX_LENGTH)]
    public string code { get; set; } = null!;

    [StringLength(LABEL_MAX_LENGTH)]
    public string label { get; set; } = null!;

    [StringLength(SYMBOL_MAX_LENGTH)]
    public string? symbol { get; set; }

    public DateTime syncedAt { get; set; } = DateTime.UtcNow;

    public const int CODE_MAX_LENGTH = 3;
    public const int LABEL_MAX_LENGTH = 200;
    public const int SYMBOL_MAX_LENGTH = 16;
}
