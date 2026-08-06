namespace Delegat.Infrastructure.Configuration;

/// <summary>
/// Tariff / feature flags from environment (Control Plane). Not editable via instance UI.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// AI assistant. Explicit <c>true</c>/<c>false</c> always win.
    /// Unset/empty: enabled for self-host backward compatibility; disabled when
    /// <c>CONTROL_PLANE_URL</c> is set (CP-managed instances must get an explicit flag).
    /// </summary>
    public static bool IsAiEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("AI_ENABLED");
        if (string.IsNullOrWhiteSpace(raw))
        {
            var cp = Environment.GetEnvironmentVariable("CONTROL_PLANE_URL");
            return string.IsNullOrWhiteSpace(cp);
        }

        return IsTruthy(raw);
    }

    /// <summary>
    /// Plugin management UI (upload/reload/delete) and custom (non-system) DLL load.
    /// Explicit <c>true</c>/<c>false</c> always win.
    /// Unset/empty: enabled for self-host; disabled when <c>CONTROL_PLANE_URL</c> is set
    /// (Ops enables via instance <c>PLUGINS_ENABLED</c>).
    /// Built-in system marketplace adapters always load regardless of this flag.
    /// </summary>
    public static bool IsPluginsEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("PLUGINS_ENABLED");
        if (string.IsNullOrWhiteSpace(raw))
        {
            var cp = Environment.GetEnvironmentVariable("CONTROL_PLANE_URL");
            return string.IsNullOrWhiteSpace(cp);
        }

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

    /// <summary>Accept Public API (/api/v1) traffic. Seed default for admin UI (default: enabled).</summary>
    public static bool IsPublicApiEnabled()
        => ParseBoolEnv("PUBLIC_API_ENABLED", defaultValue: true);

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
