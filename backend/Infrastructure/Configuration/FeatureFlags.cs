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

        return IsTruthy(raw);
    }

    /// <summary>Persist API exception rows to MySQL. Used to seed admin UI settings on first boot.</summary>
    public static bool IsErrorLoggingEnabled()
        => ParseBoolEnv("LOG_ERRORS_ENABLED", defaultValue: true);

    /// <summary>Persist Public API (/api/v1) activity logs including bodies. Seed default for admin UI.</summary>
    public static bool IsApiRequestLoggingEnabled()
        => ParseBoolEnv("LOG_API_REQUESTS_ENABLED", defaultValue: false);

    /// <summary>Persist outbound marketplace adapter HTTP exchanges. Seed default for admin UI.</summary>
    public static bool IsAdapterHttpLoggingEnabled()
        => ParseBoolEnv("LOG_ADAPTER_HTTP_ENABLED", defaultValue: false);

    public static int GetQueueRetentionDays()
    {
        var raw = Environment.GetEnvironmentVariable("QUEUE_RETENTION_DAYS");
        if (int.TryParse(raw, out var days) && days > 0)
            return Math.Clamp(days, 1, 365);
        return 7;
    }

    private static bool ParseBoolEnv(string name, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        if (IsTruthy(raw))
            return true;

        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("0", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("no", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;

        return defaultValue;
    }

    private static bool IsTruthy(string raw) =>
        raw.Equals("true", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
}
