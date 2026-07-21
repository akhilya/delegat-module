namespace Delegat.Infrastructure.Configuration;

/// <summary>
/// Machine-to-machine link to Control Plane (env-injected at provision).
/// </summary>
public sealed class PlatformLinkOptions
{
    public string? ControlPlaneUrl { get; init; }
    public string? InstanceId { get; init; }
    public string? InstanceLinkSecret { get; init; }
    public int CatalogRefreshMinutes { get; init; } = 360;
    public int HeartbeatMinutes { get; init; } = 5;

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(ControlPlaneUrl)
        && !string.IsNullOrWhiteSpace(InstanceId)
        && !string.IsNullOrWhiteSpace(InstanceLinkSecret);

    public static PlatformLinkOptions FromEnvironment()
    {
        return new PlatformLinkOptions
        {
            ControlPlaneUrl = TrimSlash(Environment.GetEnvironmentVariable("CONTROL_PLANE_URL")),
            InstanceId = NullIfEmpty(Environment.GetEnvironmentVariable("INSTANCE_ID")),
            InstanceLinkSecret = NullIfEmpty(Environment.GetEnvironmentVariable("INSTANCE_LINK_SECRET")),
            CatalogRefreshMinutes = ParsePositiveMinutes(
                Environment.GetEnvironmentVariable("PLATFORM_CATALOG_REFRESH_MINUTES"), 360),
            HeartbeatMinutes = ParsePositiveMinutes(
                Environment.GetEnvironmentVariable("PLATFORM_HEARTBEAT_MINUTES"), 5),
        };
    }

    private static string? TrimSlash(string? url)
    {
        var v = NullIfEmpty(url);
        return v?.TrimEnd('/');
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ParsePositiveMinutes(string? raw, int fallback)
    {
        if (int.TryParse(raw, out var n) && n > 0)
            return Math.Clamp(n, 1, 24 * 60);
        return fallback;
    }
}
