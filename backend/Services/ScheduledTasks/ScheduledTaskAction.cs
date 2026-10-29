using Delegat.Models;

namespace Delegat.Services.ScheduledTasks;

/// <summary>Extension point for the separately curated scheduled-action catalog.</summary>
public interface IScheduledTaskAction
{
    string Id { get; }
    string DisplayName => Id;
    string Description => string.Empty;
    Task ExecuteAsync(ScheduledTask task, CancellationToken ct);
}
