using System.ComponentModel.DataAnnotations;
using Delegat.Infrastructure.Interfaces;

namespace Delegat.Models.Marketplace;

/// <summary>
/// Last pull per marketplace connection and order status (cooldown / error).
/// </summary>
public class MarketplaceOrderSyncState : IEntity
{
    [Key]
    public int Id { get; set; }

    public int connectionId { get; set; }

    [StringLength(32)]
    public string status { get; set; } = "";

    public DateTime lastPulledAt { get; set; }

    [StringLength(2000)]
    public string? lastError { get; set; }
}
