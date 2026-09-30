using Delegat.Services.Ai;

namespace Delegat.Tests.Ai;

public class MappingExportExperienceTests
{
    private static readonly DateTime Saved = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsCurrent_DropsResultsFromBeforeTheCanvasSave()
    {
        Assert.False(MappingExportExperience.IsCurrent(Saved.AddMinutes(-1), Saved));
        Assert.True(MappingExportExperience.IsCurrent(Saved, Saved));
        Assert.True(MappingExportExperience.IsCurrent(Saved.AddMinutes(1), Saved));
        Assert.True(MappingExportExperience.IsCurrent(Saved, null));
        Assert.False(MappingExportExperience.IsCurrent(null, Saved));
    }

    [Fact]
    public void Format_AfterASaveWithNoExports_ClosesEarlierFailures()
    {
        var text = MappingExportExperience.Format(Saved, "2026-09-30 15:00", 0, 0, []);
        Assert.Contains("No exports since that save", text, StringComparison.Ordinal);
        Assert.Contains("not open problems", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ListsTheAdapterTextForFailuresSinceTheSave()
    {
        var text = MappingExportExperience.Format(
            Saved,
            "2026-09-30 15:00",
            3,
            2,
            [new MappingExportExperience.FailureGroup(2, "Shirt", "Wildberries 400: brand is unknown")]);
        Assert.Contains("Synced: 3", text, StringComparison.Ordinal);
        Assert.Contains("Failed: 2", text, StringComparison.Ordinal);
        Assert.Contains("brand is unknown", text, StringComparison.Ordinal);
        Assert.Contains("not listed", text, StringComparison.Ordinal);
    }
}
