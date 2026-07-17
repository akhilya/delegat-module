using Delegat.Infrastructure.Auth;
using Delegat.Models;

namespace Delegat.Tests.Infrastructure;

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public int? UserId { get; set; }
    public int? SelectedStoreId { get; set; }
    public User? User { get; set; }
    public bool IsAuthenticated => UserId != null;

    public int? GetUserId() => UserId;

    public Task<User?> GetUserAsync(CancellationToken ct = default) =>
        Task.FromResult(User ?? (UserId == null
            ? null
            : new User { Id = UserId.Value, UserName = $"user{UserId}", isActive = true }));

    public int? GetSelectedStoreId() => SelectedStoreId;

    public Task<int?> GetSelectedStoreIdAsync(CancellationToken ct = default) =>
        Task.FromResult(SelectedStoreId);
}
