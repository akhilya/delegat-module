using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

public sealed record PlatformEntitlementsCacheDto(
    bool Linked,
    string? PlanName,
    string? PlanSlug,
    string? SubscriptionStatus,
    DateTimeOffset? OveragePurgeAt,
    DateTime UpdatedAt);

public interface IPlatformEntitlementsService
{
    Task<PlatformEntitlementsCacheDto> GetCachedAsync(CancellationToken ct = default);
    Task UpsertAsync(PlatformEntitlementsCacheDto dto, CancellationToken ct = default);
}

public sealed class PlatformEntitlementsService : IPlatformEntitlementsService
{
    private readonly DataContext _db;

    public PlatformEntitlementsService(DataContext db)
    {
        _db = db;
    }

    public async Task<PlatformEntitlementsCacheDto> GetCachedAsync(CancellationToken ct = default)
    {
        var row = await _db.instancePlatformEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == InstancePlatformEntitlements.SingletonId, ct);

        if (row == null)
        {
            return new PlatformEntitlementsCacheDto(
                Linked: false,
                PlanName: null,
                PlanSlug: null,
                SubscriptionStatus: null,
                OveragePurgeAt: null,
                UpdatedAt: DateTime.UtcNow);
        }

        return new PlatformEntitlementsCacheDto(
            Linked: row.linked,
            PlanName: row.planName,
            PlanSlug: row.planSlug,
            SubscriptionStatus: row.subscriptionStatus,
            OveragePurgeAt: row.overagePurgeAt,
            UpdatedAt: row.updatedAt);
    }

    public async Task UpsertAsync(PlatformEntitlementsCacheDto dto, CancellationToken ct = default)
    {
        var row = await _db.instancePlatformEntitlements
            .FirstOrDefaultAsync(x => x.Id == InstancePlatformEntitlements.SingletonId, ct);

        if (row == null)
        {
            row = new InstancePlatformEntitlements { Id = InstancePlatformEntitlements.SingletonId };
            _db.instancePlatformEntitlements.Add(row);
        }

        row.linked = dto.Linked;
        row.planName = dto.PlanName;
        row.planSlug = dto.PlanSlug;
        row.subscriptionStatus = dto.SubscriptionStatus;
        row.overagePurgeAt = dto.OveragePurgeAt;
        row.updatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
