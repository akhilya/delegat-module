using System.Reflection;

namespace Delegat;

/// <summary>
/// Instance product SemVer. Prefer assembly InformationalVersion (Docker APP_VERSION /
/// MSBuild from root VERSION); never use a hardcoded release string.
/// </summary>
public static class PlatformVersion
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var informational =
            GetInformationalVersion(Assembly.GetEntryAssembly())
            ?? GetInformationalVersion(typeof(PlatformVersion).Assembly);

        var cleaned = Clean(informational);
        return cleaned ?? "dev";
    }

    private static string? GetInformationalVersion(Assembly? assembly) =>
        assembly
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

    /// <summary>
    /// Strip SemVer build metadata and a leading "v"; treat empty / "dev" as unset.
    /// </summary>
    internal static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var v = value.Trim();
        var plus = v.IndexOf('+');
        if (plus >= 0)
            v = v[..plus];

        if (v.Length > 1
            && (v[0] == 'v' || v[0] == 'V')
            && char.IsDigit(v[1]))
        {
            v = v[1..];
        }

        if (string.IsNullOrWhiteSpace(v)
            || string.Equals(v, "dev", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "vdev", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return v;
    }
}
