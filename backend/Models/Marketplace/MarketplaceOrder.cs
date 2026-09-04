using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models.Marketplace;

/// <summary>
/// Stored marketplace order snapshot: denormalized list columns plus full ExternalOrder JSON.
/// </summary>
public class MarketplaceOrder : IEntity
{
    [Key]
    public int Id { get; set; }

    public int storeId { get; set; }

    public int connectionId { get; set; }

    [StringLength(128)]
    public string marketplaceOrderId { get; set; } = "";

    [StringLength(128)]
    public string? displayId { get; set; }

    [StringLength(32)]
    public string status { get; set; } = "";

    [StringLength(16)]
    public string channel { get; set; } = "shop";

    [StringLength(8)]
    public string currency { get; set; } = "";

    public decimal? total { get; set; }

    public DateTime? orderCreatedAt { get; set; }

    public DateTime? orderUpdatedAt { get; set; }

    public DateTime syncedAt { get; set; }

    [Column(TypeName = "longtext")]
    public string payloadJson { get; set; } = "";
}
