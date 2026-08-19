using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>
/// Shared rules for operation status. Pause is no longer a live control;
/// leftover <c>paused</c> rows are treated as a user stop.
/// </summary>
public static class OperationLifecycle
{
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Partial = "partial";

    public static bool IsStopRequested(string? status) =>
        status is Cancelled or Paused;

    public static bool IsTerminal(string? status) =>
        status is Completed or Failed or Partial or Cancelled;

    public static bool IsActive(string? status) =>
        status is Running or Paused;

    /// <summary>
    /// Cancelled is sticky so Stop cannot be overwritten by a worker finishing the in-flight item.
    /// Other terminal statuses keep the first finish, except cancelled may replace them when Stop wins a race.
    /// </summary>
    public static bool ShouldApplyFinish(string? currentStatus, string incomingStatus)
    {
        if (string.Equals(currentStatus, Cancelled, StringComparison.Ordinal))
            return string.Equals(incomingStatus, Cancelled, StringComparison.Ordinal);
        if (IsTerminal(currentStatus) && !string.Equals(incomingStatus, Cancelled, StringComparison.Ordinal))
            return false;
        return true;
    }

    public static async Task<string?> GetStatusAsync(
        DataContext db, string operationId, CancellationToken ct) =>
        await db.operations.AsNoTracking()
            .Where(o => o.operationId == operationId)
            .Select(o => o.status)
            .FirstOrDefaultAsync(ct);

    public static async Task<bool> IsStopRequestedAsync(
        DataContext db, string operationId, CancellationToken ct) =>
        IsStopRequested(await GetStatusAsync(db, operationId, ct));
}
