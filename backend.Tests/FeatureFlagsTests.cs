using Delegat.Infrastructure.Configuration;

namespace Delegat.Tests;

public class FeatureFlagsTests
{
    private static IDisposable SetEnv(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestore(name, previous);
    }

    private sealed class EnvRestore(string name, string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    public void IsAiEnabled_ParsesEnv(string? raw, bool expected)
    {
        using var _ = SetEnv("AI_ENABLED", raw);
        Assert.Equal(expected, FeatureFlags.IsAiEnabled());
    }

    [Theory]
    [InlineData(null, 7)]
    [InlineData("0", 7)]
    [InlineData("nope", 7)]
    [InlineData("1", 1)]
    [InlineData("30", 30)]
    [InlineData("365", 365)]
    [InlineData("999", 365)] // clamped
    public void GetQueueRetentionDays_ParsesAndClamps(string? raw, int expected)
    {
        using var _ = SetEnv("QUEUE_RETENTION_DAYS", raw);
        Assert.Equal(expected, FeatureFlags.GetQueueRetentionDays());
    }
}
