namespace Delegat.Infrastructure.Configuration;

/// <summary>
/// Tariff / feature flags from environment (Control Plane). Not editable via instance UI.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// AI assistant. Unset defaults to enabled for backward compatibility;
    /// set <c>AI_ENABLED=false</c> to disable (Control Plane free tier).
    /// </summary>
    public static bool IsAiEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("AI_ENABLED");
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        return raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    public static int GetQueueRetentionDays()
    {
        var raw = Environment.GetEnvironmentVariable("QUEUE_RETENTION_DAYS");
        if (int.TryParse(raw, out var days) && days > 0)
            return Math.Clamp(days, 1, 365);
        return 7;
    }
}
