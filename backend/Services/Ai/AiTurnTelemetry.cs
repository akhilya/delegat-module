using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat.Models.Ai;
using Sentry;

namespace Delegat.Services.Ai;

/// <summary>
/// In-memory, content-free measurements for one agent turn. No prompt, user text,
/// tool arguments, tool results, session id, or user id are written to the log.
/// </summary>
internal sealed class AiTurnTelemetry : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ILogger _logger;
    private readonly int _storeId;
    private readonly string _provider;
    private readonly string _model;
    private readonly string _promptVersion;
    private readonly ITransactionTracer? _trace;
    private readonly HashSet<string> _seenToolCallSignatures = new(StringComparer.Ordinal);
    private readonly Stopwatch _turnTimer = Stopwatch.StartNew();
    private int _modelRounds;
    private int _toolRounds;
    private int _modelToolCalls;
    private int _repeatedModelToolCalls;
    private int _executedTools;
    private int _failedTools;
    private int _usageRounds;
    private int _historyTurnsRemoved;
    private long _historyCharactersRemoved;
    private bool _historyBudgetExceeded;
    private long _modelLatencyMs;
    private long _toolLatencyMs;
    private long _inputCharacters;
    private long _outputCharacters;
    private long? _inputTokens;
    private long? _outputTokens;
    private long? _cacheReadInputTokens;
    private long? _cacheCreationInputTokens;
    private string? _outcome;
    private bool _disposed;

    public AiTurnTelemetry(ILogger logger, int storeId, string provider, string model, string promptVersion)
    {
        _logger = logger;
        _storeId = storeId;
        _provider = provider;
        _model = model;
        _promptVersion = promptVersion;
        _trace = StartTrace(logger, provider, model, promptVersion);
    }

    public AiTurnSpan StartModelRound()
    {
        var span = StartChild("gen_ai.request", "model call");
        span.SetTag("ai.provider", _provider);
        span.SetTag("ai.model", _model);
        return span;
    }

    public AiTurnSpan StartToolExecution(string toolName)
    {
        var span = StartChild("ai.tool", "tool execution");
        span.SetTag("ai.tool.name", toolName);
        return span;
    }

    public static long EstimateInputCharacters(AiRequest request)
    {
        long count = request.system?.Length ?? 0;
        foreach (var message in request.messages)
        {
            count += message.role?.Length ?? 0;
            count += SerializedLength(message.content);
        }

        if (request.tools != null)
        {
            foreach (var tool in request.tools)
            {
                count += tool.name?.Length ?? 0;
                count += tool.description?.Length ?? 0;
                count += SerializedLength(AiApiClient.ToolSchemaElement(tool.input_schema));
            }
        }

        return count;
    }

    public void RecordModelToolCalls(IEnumerable<(string Name, string InputJson)> calls)
    {
        var roundCallCount = 0;
        foreach (var (name, inputJson) in calls)
        {
            roundCallCount++;
            var signatureBytes = Encoding.UTF8.GetBytes($"{name}\0{NormalizeToolInput(inputJson)}");
            var signature = Convert.ToHexString(SHA256.HashData(signatureBytes));
            if (!_seenToolCallSignatures.Add(signature))
                _repeatedModelToolCalls++;
        }

        if (roundCallCount == 0) return;
        _toolRounds++;
        _modelToolCalls += roundCallCount;
    }

    public void RecordHistoryTrim(int turnsRemoved, long charactersRemoved, bool budgetExceeded)
    {
        _historyTurnsRemoved = Math.Max(0, turnsRemoved);
        _historyCharactersRemoved = Math.Max(0, charactersRemoved);
        _historyBudgetExceeded = budgetExceeded;
    }

    public void RecordModelRound(
        long elapsedMs, long inputCharacters, long outputCharacters, AiStreamUsage usage)
    {
        _modelRounds++;
        _modelLatencyMs += Math.Max(0, elapsedMs);
        _inputCharacters += Math.Max(0, inputCharacters);
        _outputCharacters += Math.Max(0, outputCharacters);

        if (usage.inputTokens.HasValue || usage.outputTokens.HasValue
            || usage.cacheReadInputTokens.HasValue || usage.cacheCreationInputTokens.HasValue)
            _usageRounds++;

        _inputTokens = Add(_inputTokens, usage.inputTokens);
        _outputTokens = Add(_outputTokens, usage.outputTokens);
        _cacheReadInputTokens = Add(_cacheReadInputTokens, usage.cacheReadInputTokens);
        _cacheCreationInputTokens = Add(_cacheCreationInputTokens, usage.cacheCreationInputTokens);
    }

    public void RecordToolExecution(long elapsedMs, bool failed)
    {
        _executedTools++;
        _toolLatencyMs += Math.Max(0, elapsedMs);
        if (failed) _failedTools++;
    }

    public void Complete(string outcome) => _outcome = outcome;

    public void CompleteIfUnset(string outcome) => _outcome ??= outcome;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _turnTimer.Stop();

        _logger.LogInformation(
            "AI chat turn telemetry: StoreId={StoreId} Provider={Provider} Model={Model} PromptVersion={PromptVersion} Outcome={Outcome} DurationMs={DurationMs} ModelRounds={ModelRounds} ToolRounds={ToolRounds} ModelToolCalls={ModelToolCalls} RepeatedModelToolCalls={RepeatedModelToolCalls} ExecutedTools={ExecutedTools} FailedTools={FailedTools} ModelLatencyMs={ModelLatencyMs} ToolLatencyMs={ToolLatencyMs} InputCharacters={InputCharacters} OutputCharacters={OutputCharacters} HistoryTurnsRemoved={HistoryTurnsRemoved} HistoryCharactersRemoved={HistoryCharactersRemoved} HistoryBudgetExceeded={HistoryBudgetExceeded} UsageRounds={UsageRounds} InputTokens={InputTokens} OutputTokens={OutputTokens} CacheReadInputTokens={CacheReadInputTokens} CacheCreationInputTokens={CacheCreationInputTokens}",
            _storeId,
            _provider,
            _model,
            _promptVersion,
            _outcome ?? "interrupted",
            _turnTimer.ElapsedMilliseconds,
            _modelRounds,
            _toolRounds,
            _modelToolCalls,
            _repeatedModelToolCalls,
            _executedTools,
            _failedTools,
            _modelLatencyMs,
            _toolLatencyMs,
            _inputCharacters,
            _outputCharacters,
            _historyTurnsRemoved,
            _historyCharactersRemoved,
            _historyBudgetExceeded,
            _usageRounds,
            _inputTokens,
            _outputTokens,
            _cacheReadInputTokens,
            _cacheCreationInputTokens);

        FinishTrace();
    }

    private static ITransactionTracer? StartTrace(
        ILogger logger, string provider, string model, string promptVersion)
    {
        if (!SentrySdk.IsEnabled) return null;

        try
        {
            var trace = SentrySdk.StartTransaction("ai.chat.turn", "ai.turn");
            trace.SetTag("ai.provider", provider);
            trace.SetTag("ai.model", model);
            trace.SetTag("ai.prompt_version", promptVersion);
            return trace;
        }
        catch
        {
            // Observability must never prevent an AI turn from running.
            logger.LogDebug("Could not start the optional AI telemetry trace.");
            return null;
        }
    }

    private AiTurnSpan StartChild(string operation, string description)
    {
        try
        {
            return new AiTurnSpan(_trace?.StartChild(operation, description));
        }
        catch
        {
            return new AiTurnSpan(null);
        }
    }

    private void FinishTrace()
    {
        if (_trace == null) return;

        try
        {
            _trace.SetTag("ai.outcome", _outcome ?? "interrupted");
            _trace.SetTag("ai.history_budget_exceeded", _historyBudgetExceeded.ToString());
            _trace.SetMeasurement("ai.model.tool_calls", _modelToolCalls);
            _trace.SetMeasurement("ai.model.repeated_tool_calls", _repeatedModelToolCalls);
            _trace.SetMeasurement("ai.tool.executions", _executedTools);
            _trace.SetMeasurement("ai.tool.failures", _failedTools);
            _trace.SetMeasurement("ai.history.turns_removed", _historyTurnsRemoved);
            if (_inputTokens.HasValue) _trace.SetMeasurement("ai.tokens.input", _inputTokens.Value);
            if (_outputTokens.HasValue) _trace.SetMeasurement("ai.tokens.output", _outputTokens.Value);
            if (_cacheReadInputTokens.HasValue)
                _trace.SetMeasurement("ai.tokens.cache_read", _cacheReadInputTokens.Value);
            if (_cacheCreationInputTokens.HasValue)
                _trace.SetMeasurement("ai.tokens.cache_creation", _cacheCreationInputTokens.Value);
        }
        catch
        {
            // Do not let an optional telemetry sink fail the model response.
        }
        finally
        {
            try { _trace.Finish(); }
            catch { /* Telemetry shutdown is best-effort. */ }
        }
    }

    private static long? Add(long? current, long? value) =>
        value.HasValue ? (current ?? 0) + value.Value : current;

    private static string NormalizeToolInput(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch (JsonException)
        {
            return inputJson.Trim();
        }
    }

    private static int SerializedLength<T>(T value)
    {
        try
        {
            return JsonSerializer.Serialize(value, JsonOptions).Length;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Telemetry estimates must never prevent a model request from running.
            return 0;
        }
    }
}

internal sealed class AiTurnSpan(ISpan? span) : IDisposable
{
    private ISpan? _span = span;

    public void SetOutcome(string outcome)
        => SetTag("ai.outcome", outcome);

    public void SetTag(string key, string value)
    {
        try { _span?.SetTag(key, value); }
        catch { /* Telemetry must not affect the AI turn. */ }
    }

    public void Finish()
    {
        var current = Interlocked.Exchange(ref _span, null);
        try { current?.Finish(); }
        catch { /* Telemetry must not affect the AI turn. */ }
    }

    public void Dispose() => Finish();
}
