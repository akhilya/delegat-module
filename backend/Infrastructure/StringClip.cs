namespace Delegat.Infrastructure;

/// <summary>
/// Trims and clips strings to a maximum length so marketplace import
/// never hits MySQL "Data too long for column".
/// </summary>
public static class StringClip
{
    public static string Clip(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        var s = (value ?? string.Empty).Trim();
        return s.Length <= maxLength ? s : s[..maxLength];
    }

    public static string? ClipOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Clip(value, maxLength);
    }

    public static string ClipRequired(string? value, int maxLength, string fallback)
    {
        var s = Clip(value, maxLength);
        return s.Length > 0 ? s : Clip(fallback, maxLength);
    }
}
