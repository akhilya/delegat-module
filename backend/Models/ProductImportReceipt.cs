using System.ComponentModel.DataAnnotations;
using Delegat.Models.Products;

namespace Delegat.Models;

/// <summary>Idempotency receipt for a public-card copy within one durable operation.</summary>
public class ProductImportReceipt
{
    [Key, MaxLength(64)]
    public string key { get; set; } = null!;
    public int productId { get; set; }
    public Product product { get; set; } = null!;
}
