namespace Delegat.Models;

/// <summary>A recurring store-scoped request for a registered scheduled action.</summary>
public sealed class ScheduledTask
{
    public int Id { get; set; }
    public int storeId { get; set; }
    public int authorUserId { get; set; }
    public string name { get; set; } = string.Empty;
    public string actionId { get; set; } = string.Empty;
    public string parametersJson { get; set; } = "{}";
    public int intervalMinutes { get; set; }
    public string timeZoneId { get; set; } = "UTC";
    public bool isEnabled { get; set; } = true;
    public DateTime nextRunAt { get; set; }
    public DateTime? executionLeaseExpiresAtUtc { get; set; }
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One durable attempt to execute a scheduled task.</summary>
public sealed class ScheduledTaskRun
{
    public long Id { get; set; }
    public int scheduledTaskId { get; set; }
    public int storeId { get; set; }
    public string actionId { get; set; } = string.Empty;
    public string status { get; set; } = "running";
    public string? error { get; set; }
    public DateTime scheduledFor { get; set; }
    public DateTime startedAt { get; set; } = DateTime.UtcNow;
    public DateTime? finishedAt { get; set; }
}
