using Delegat.Controllers;

namespace Delegat.Services.ScheduledTasks;

public static class ScheduledTaskInputValidator
{
    public static bool IsValid(ScheduledTaskInput input, out string? error)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 120)
            error = "Name is required and must be at most 120 characters.";
        else if (string.IsNullOrWhiteSpace(input.ActionId) || input.ActionId.Trim().Length > 160)
            error = "ActionId is required and must be at most 160 characters.";
        else if (input.IntervalMinutes is < 1 or > 525600)
            error = "IntervalMinutes must be between 1 and 525600.";
        else if (string.IsNullOrWhiteSpace(input.TimeZoneId) || input.TimeZoneId.Trim().Length > 100
                 || !TimeZoneInfo.TryFindSystemTimeZoneById(input.TimeZoneId.Trim(), out _))
            error = "TimeZoneId must be a valid system time-zone identifier.";
        else
            error = null;
        return error == null;
    }
}
