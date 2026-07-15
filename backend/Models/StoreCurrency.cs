using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Additional store currency with exchange rate to the store base currency.
/// The base currency itself is implicit with rate 1 and is not stored in this table.
/// </summary>
public class StoreCurrency
{
    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(store))]
    public int storeId { get; set; }

    public Store store { get; set; } = null!;

    /// <summary>ISO 4217 code (USD, EUR, KZT…). Not the store base currency.</summary>
    [StringLength(3)]
    public string code { get; set; } = null!;

    /// <summary>How many units of the store base currency for 1 unit of this currency.</summary>
    [Column(TypeName = "decimal(18,6)")]
    public decimal rateToBase { get; set; }

    public bool isEnabled { get; set; } = true;

    public DateTime updatedAt { get; set; } = DateTime.UtcNow;

    public const int CODE_MAX_LENGTH = 3;
}