using Delegat.Services;

namespace Delegat.Tests;

public class OperationLifecycleTests
{
    [Theory]
    [InlineData(null, "cancelled", true)]
    [InlineData("running", "cancelled", true)]
    [InlineData("paused", "cancelled", true)]
    [InlineData("cancelled", "cancelled", true)]
    [InlineData("cancelled", "completed", false)]
    [InlineData("cancelled", "partial", false)]
    [InlineData("cancelled", "failed", false)]
    [InlineData("completed", "failed", false)]
    [InlineData("completed", "cancelled", true)]
    [InlineData("failed", "completed", false)]
    public void ShouldApplyFinish_CancelledIsSticky(string? current, string incoming, bool expected)
    {
        Assert.Equal(expected, OperationLifecycle.ShouldApplyFinish(current, incoming));
    }

    [Fact]
    public void IsStopRequested_TreatsLegacyPausedAsStop()
    {
        Assert.True(OperationLifecycle.IsStopRequested("cancelled"));
        Assert.True(OperationLifecycle.IsStopRequested("paused"));
        Assert.False(OperationLifecycle.IsStopRequested("running"));
    }
}
