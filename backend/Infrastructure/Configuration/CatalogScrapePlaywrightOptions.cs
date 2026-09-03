namespace Delegat.Infrastructure.Configuration;

/// <summary>
/// Catalog scrape Chromium fallback (Playwright). HTTP unofficial JSON stays first.
/// </summary>
public sealed class CatalogScrapePlaywrightOptions
{
    public const int DefaultTimeoutMs = 60_000;
    public const int DefaultConcurrency = 1;
    public const string DefaultChannel = "chromium";

    public bool Enabled { get; init; } = true;
    public bool Headless { get; init; } = true;
    public bool InstallIfMissing { get; init; } = true;
    public string? Proxy { get; init; }
    /// <summary>Playwright channel. Default chromium = full Chrome headless (not headless-shell).</summary>
    public string? Channel { get; init; } = DefaultChannel;
    public int TimeoutMs { get; init; } = DefaultTimeoutMs;
    public int MaxConcurrentPages { get; init; } = DefaultConcurrency;

    public static CatalogScrapePlaywrightOptions FromEnvironment()
        => From(Environment.GetEnvironmentVariable);

    internal static CatalogScrapePlaywrightOptions From(Func<string, string?> env)
    {
        return new CatalogScrapePlaywrightOptions
        {
            Enabled = ParseBool(env("CATALOG_SCRAPE_PLAYWRIGHT"), defaultValue: true),
            Headless = ParseBool(env("CATALOG_SCRAPE_PLAYWRIGHT_HEADLESS"), defaultValue: true),
            InstallIfMissing = ParseBool(env("CATALOG_SCRAPE_PLAYWRIGHT_INSTALL"), defaultValue: true),
            Proxy = NullIfEmpty(env("CATALOG_SCRAPE_PLAYWRIGHT_PROXY")),
            Channel = ParseChannel(env("CATALOG_SCRAPE_PLAYWRIGHT_CHANNEL")),
            TimeoutMs = ParseInt(
                env("CATALOG_SCRAPE_PLAYWRIGHT_TIMEOUT_MS"), DefaultTimeoutMs, min: 5_000, max: 180_000),
            MaxConcurrentPages = ParseInt(
                env("CATALOG_SCRAPE_PLAYWRIGHT_CONCURRENCY"), DefaultConcurrency, min: 1, max: 4)
        };
    }

    internal static PlaywrightProxySettings? ParseProxy(string? raw)
    {
        var value = NullIfEmpty(raw);
        if (value == null)
            return null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return new PlaywrightProxySettings(value, null, null);
        }

        var port = uri.IsDefaultPort ? (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80) : uri.Port;
        var server = $"{uri.Scheme}://{uri.Host}:{port}";
        string? user = null;
        string? password = null;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            user = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
                password = Uri.UnescapeDataString(parts[1]);
        }

        return new PlaywrightProxySettings(server, user, password);
    }

    private static string? NullIfEmpty(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    /// <summary>
    /// Unset → chromium (new headless). <c>none</c>/<c>off</c> → bundled headless-shell.
    /// </summary>
    internal static string? ParseChannel(string? raw)
    {
        var value = NullIfEmpty(raw);
        if (value == null)
            return DefaultChannel;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return null;
        return value.ToLowerInvariant();
    }

    private static bool ParseBool(string? raw, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("0", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("no", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;
        return defaultValue;
    }

    private static int ParseInt(string? raw, int fallback, int min, int max)
    {
        if (!int.TryParse(raw, out var n))
            return fallback;
        return Math.Clamp(n, min, max);
    }
}

internal sealed record PlaywrightProxySettings(string Server, string? Username, string? Password);
