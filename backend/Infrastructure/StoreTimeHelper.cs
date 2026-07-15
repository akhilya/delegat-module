namespace Delegat.Infrastructure;

public static class StoreTimeHelper
{
    public const string DefaultTimezone = "Europe/Moscow";

    public static TimeZoneInfo ResolveTimeZone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            timezoneId = DefaultTimezone;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimezone);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimezone);
        }
    }

    public static DateTime ToStoreLocal(DateTime utc, string? timezoneId)
    {
        var utcKind = utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            : utc.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeFromUtc(utcKind, ResolveTimeZone(timezoneId));
    }

    public static string FormatInStoreTimezone(DateTime utc, string? timezoneId, string format = "yyyy-MM-dd HH:mm")
    {
        var local = ToStoreLocal(utc, timezoneId);
        var tz = ResolveTimeZone(timezoneId);
        var offset = tz.GetUtcOffset(local);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var abs = offset.Duration();
        var offsetLabel = $"UTC{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
        return $"{local.ToString(format)} {offsetLabel}";
    }

    public static bool IsValidTimezoneId(string timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return true;
        }
        catch
        {
            return false;
        }
    }
}