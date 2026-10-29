using System.Globalization;
using Microsoft.Extensions.Configuration;
using Sentry;

namespace Delegat.Services.Ai;

internal static class AiTelemetrySink
{
    public const string TraceSampleRateSetting = "AI_TELEMETRY_TRACE_SAMPLE_RATE";
    private const float DefaultTraceSampleRate = 0.1f;

    public static IDisposable? TryInitialize(IConfiguration configuration)
    {
        var dsn = configuration["SENTRY_DSN"];
        if (string.IsNullOrWhiteSpace(dsn)) return null;

        var rawSampleRate = configuration[TraceSampleRateSetting];
        var sampleRate = string.IsNullOrWhiteSpace(rawSampleRate)
            ? DefaultTraceSampleRate
            : ParseSampleRate(rawSampleRate);

        return SentrySdk.Init(options =>
        {
            options.Dsn = dsn.Trim();
            options.TracesSampleRate = sampleRate;
            options.SampleRate = 0f;
            options.SendDefaultPii = false;
            options.AutoSessionTracking = false;
            options.IsGlobalModeEnabled = false;
        });
    }

    internal static float ParseSampleRate(string rawSampleRate)
    {
        if (!float.TryParse(rawSampleRate, NumberStyles.Float, CultureInfo.InvariantCulture, out var sampleRate)
            || !float.IsFinite(sampleRate)
            || sampleRate is < 0 or > 1)
            throw new InvalidOperationException(
                $"{TraceSampleRateSetting} must be a number between 0 and 1.");

        return sampleRate;
    }
}
