using Delegat.Services.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Delegat.Tests.Ai;

public class AiTurnTelemetryTests
{
    [Fact]
    public void OptionalSentrySinkIsDisabledWithoutDsn()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Null(AiTelemetrySink.TryInitialize(configuration));
        Assert.False(Sentry.SentrySdk.IsEnabled);
    }

    [Theory]
    [InlineData("0", 0f)]
    [InlineData("0.1", 0.1f)]
    [InlineData("1", 1f)]
    public void TraceSampleRateAcceptsOnlyTheConfiguredRange(string value, float expected)
    {
        Assert.Equal(expected, AiTelemetrySink.ParseSampleRate(value));
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("NaN")]
    [InlineData("many")]
    public void TraceSampleRateRejectsInvalidValues(string value)
    {
        Assert.Throws<InvalidOperationException>(() => AiTelemetrySink.ParseSampleRate(value));
    }

    [Fact]
    public void Dispose_LogsAggregatedCountsAndProviderUsageWithoutContent()
    {
        var logger = new CapturingLogger();
        using (var telemetry = new AiTurnTelemetry(logger, 7, "Anthropic", "test-model", "chat-test-v1"))
        {
            telemetry.RecordModelRound(12, 1000, 100, new AiStreamUsage
            {
                inputTokens = 50,
                outputTokens = 10,
                cacheReadInputTokens = 20,
            });
            telemetry.RecordModelRound(18, 800, 80, new AiStreamUsage
            {
                inputTokens = 30,
                outputTokens = 8,
                cacheCreationInputTokens = 5,
            });
            telemetry.RecordModelToolCalls([
                ("search_catalog", "{\"query\":\"private prompt\"}"),
                ("search_catalog", "{ \"query\": \"private prompt\" }"),
            ]);
            telemetry.RecordModelToolCalls([
                ("search_catalog", "{\"query\":\"private prompt\"}"),
                ("search_catalog", "{\"query\":\"different\"}"),
            ]);
            telemetry.RecordHistoryTrim(3, 2500, budgetExceeded: false);
            telemetry.RecordToolExecution(40, failed: false);
            telemetry.RecordToolExecution(10, failed: true);
            telemetry.Complete("completed");
        }

        var message = Assert.Single(logger.Messages);
        Assert.Contains("Outcome=completed", message, StringComparison.Ordinal);
        Assert.Contains("PromptVersion=chat-test-v1", message, StringComparison.Ordinal);
        Assert.Contains("ModelRounds=2", message, StringComparison.Ordinal);
        Assert.Contains("ToolRounds=2", message, StringComparison.Ordinal);
        Assert.Contains("ModelToolCalls=4", message, StringComparison.Ordinal);
        Assert.Contains("RepeatedModelToolCalls=2", message, StringComparison.Ordinal);
        Assert.Contains("ExecutedTools=2", message, StringComparison.Ordinal);
        Assert.Contains("FailedTools=1", message, StringComparison.Ordinal);
        Assert.Contains("ModelLatencyMs=30", message, StringComparison.Ordinal);
        Assert.Contains("ToolLatencyMs=50", message, StringComparison.Ordinal);
        Assert.Contains("InputCharacters=1800", message, StringComparison.Ordinal);
        Assert.Contains("HistoryTurnsRemoved=3", message, StringComparison.Ordinal);
        Assert.Contains("HistoryCharactersRemoved=2500", message, StringComparison.Ordinal);
        Assert.Contains("HistoryBudgetExceeded=False", message, StringComparison.Ordinal);
        Assert.Contains("InputTokens=80", message, StringComparison.Ordinal);
        Assert.Contains("OutputTokens=18", message, StringComparison.Ordinal);
        Assert.Contains("CacheReadInputTokens=20", message, StringComparison.Ordinal);
        Assert.Contains("CacheCreationInputTokens=5", message, StringComparison.Ordinal);
        Assert.DoesNotContain("private prompt", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AiStreamUsage_MergeLatestDoesNotDoubleCountRepeatedCumulativeUpdates()
    {
        var usage = new AiStreamUsage { inputTokens = 100, outputTokens = 4 };

        usage.MergeLatest(new AiStreamUsage { inputTokens = 100, outputTokens = 12 });
        usage.MergeLatest(new AiStreamUsage { inputTokens = 80, outputTokens = 10, cacheReadInputTokens = 20 });

        Assert.Equal(100, usage.inputTokens);
        Assert.Equal(12, usage.outputTokens);
        Assert.Equal(20, usage.cacheReadInputTokens);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
