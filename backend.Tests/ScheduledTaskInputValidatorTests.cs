using System.Text.Json;
using Delegat.Controllers;
using Delegat.Services.ScheduledTasks;

namespace Delegat.Tests;

public sealed class ScheduledTaskInputValidatorTests
{
    [Fact]
    public void AcceptsValidIntervalTask()
    {
        var input = new ScheduledTaskInput
        {
            Name = "Update prices",
            ActionId = "marketplace.update-prices",
            IntervalMinutes = 30,
            TimeZoneId = "UTC",
            Parameters = new Dictionary<string, JsonElement>(),
        };

        Assert.True(ScheduledTaskInputValidator.IsValid(input, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(525601)]
    public void RejectsIntervalOutsideSupportedRange(int intervalMinutes)
    {
        var input = new ScheduledTaskInput
        {
            Name = "Task",
            ActionId = "task.action",
            IntervalMinutes = intervalMinutes,
            TimeZoneId = "UTC",
        };

        Assert.False(ScheduledTaskInputValidator.IsValid(input, out var error));
        Assert.Contains("IntervalMinutes", error);
    }

    [Fact]
    public void RejectsUnknownTimeZone()
    {
        var input = new ScheduledTaskInput
        {
            Name = "Task",
            ActionId = "task.action",
            IntervalMinutes = 5,
            TimeZoneId = "Not/A_Timezone",
        };

        Assert.False(ScheduledTaskInputValidator.IsValid(input, out var error));
        Assert.Contains("TimeZoneId", error);
    }
}
