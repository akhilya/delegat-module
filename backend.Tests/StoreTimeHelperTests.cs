using Delegat.Infrastructure;

namespace Delegat.Tests;

public class StoreTimeHelperTests
{
    [Fact]
    public void ResolveTimeZone_NullOrBlank_UsesMoscowDefault()
    {
        var tz = StoreTimeHelper.ResolveTimeZone(null);
        Assert.Equal(StoreTimeHelper.DefaultTimezone, tz.Id);

        var blank = StoreTimeHelper.ResolveTimeZone("   ");
        Assert.Equal(StoreTimeHelper.DefaultTimezone, blank.Id);
    }

    [Fact]
    public void ResolveTimeZone_UnknownId_FallsBackToDefault()
    {
        var tz = StoreTimeHelper.ResolveTimeZone("Not/ARealZone");
        Assert.Equal(StoreTimeHelper.DefaultTimezone, tz.Id);
    }

    [Fact]
    public void ResolveTimeZone_ValidIana_ReturnsMatchingZone()
    {
        var tz = StoreTimeHelper.ResolveTimeZone("Asia/Yekaterinburg");
        Assert.Equal("Asia/Yekaterinburg", tz.Id);
    }

    [Fact]
    public void ToStoreLocal_ConvertsUtcToMoscow()
    {
        // 2026-01-15 12:00 UTC → 15:00 Moscow (UTC+3, no DST)
        var utc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var local = StoreTimeHelper.ToStoreLocal(utc, "Europe/Moscow");

        Assert.Equal(new DateTime(2026, 1, 15, 15, 0, 0), local);
    }

    [Fact]
    public void ToStoreLocal_UnspecifiedKind_TreatedAsUtc()
    {
        var unspecified = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var local = StoreTimeHelper.ToStoreLocal(unspecified, "Europe/Moscow");

        Assert.Equal(new DateTime(2026, 1, 15, 15, 0, 0), local);
    }

    [Fact]
    public void FormatInStoreTimezone_IncludesOffsetLabel()
    {
        var utc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var formatted = StoreTimeHelper.FormatInStoreTimezone(utc, "Europe/Moscow");

        Assert.Equal("2026-01-15 15:00 UTC+03:00", formatted);
    }

    [Theory]
    [InlineData("Europe/Moscow", true)]
    [InlineData("UTC", true)]
    [InlineData("Not/ARealZone", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidTimezoneId_ValidatesIanaIds(string id, bool expected)
    {
        Assert.Equal(expected, StoreTimeHelper.IsValidTimezoneId(id));
    }
}
