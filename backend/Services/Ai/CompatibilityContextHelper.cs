using System.Text.Json;
using System.Text.Json.Serialization;
using static Delegat.Services.Ai.MappingCompatibilityService;

namespace Delegat.Services.Ai;

/// <summary>
/// Read/write <c>CategoryMapping.compatibilityContextJson</c> — single source of truth
/// for compatibility analysis, survey questions, and user answers.
/// </summary>
public static class CompatibilityContextHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const string ContextVersion = "2";

    public class StoredContext
    {
        public string Version { get; set; } = ContextVersion;
        public DateTime? AnalyzedAt { get; set; }
        public List<CompatibilityQuestion> Questions { get; set; } = [];
        public CompatibilityReport? Report { get; set; }
        public Dictionary<string, string> Answers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime? SurveyCompletedAt { get; set; }
    }

    public static StoredContext? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<StoredContext>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static string Serialize(StoredContext ctx) =>
        JsonSerializer.Serialize(ctx, JsonOpts);

    public static bool IsAnalyzed(StoredContext? ctx) =>
        ctx?.AnalyzedAt != null && ctx.Report != null;

    public static IEnumerable<CompatibilityQuestion> PendingQuestions(StoredContext? ctx)
    {
        if (ctx?.Questions == null) yield break;
        foreach (var q in ctx.Questions)
        {
            var key = q.AdapterFieldKey;
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!ctx.Answers.ContainsKey(key))
                yield return q;
        }
    }

    public static bool HasPendingSurvey(StoredContext? ctx) =>
        PendingQuestions(ctx).Any();

    public static bool AreAllQuestionsAnswered(StoredContext? ctx, IReadOnlyDictionary<string, string>? answers)
    {
        if (ctx == null || !HasPendingSurvey(ctx)) return true;
        if (answers == null || answers.Count == 0) return false;
        foreach (var q in PendingQuestions(ctx))
        {
            var key = q.AdapterFieldKey!;
            if (!answers.TryGetValue(key, out var val) || string.IsNullOrWhiteSpace(val))
                return false;
        }
        return true;
    }

    public static string SurveyStatus(StoredContext? ctx)
    {
        if (!IsAnalyzed(ctx)) return "not_analyzed";
        if (HasPendingSurvey(ctx)) return "pending_survey";
        return "survey_done";
    }

    public static CompatibilityAnalysisResult ToAnalyzeResult(int mappingId, StoredContext ctx)
    {
        var questions = HasPendingSurvey(ctx)
            ? PendingQuestions(ctx).ToList()
            : [];

        return new CompatibilityAnalysisResult
        {
            MappingId = mappingId,
            AnalyzedAt = ctx.AnalyzedAt ?? DateTime.UtcNow,
            Report = ctx.Report ?? new CompatibilityReport { MappingId = mappingId },
            Questions = questions,
        };
    }

    public static string AnalyzeSummary(StoredContext ctx)
    {
        var pending = PendingQuestions(ctx).Count();
        return pending > 0
            ? $"{pending} вопросов совместимости"
            : "Совместимость проверена, вопросов нет";
    }

    public static StoredContext MergeAnswers(StoredContext existing, Dictionary<string, string> newAnswers)
    {
        foreach (var (k, v) in newAnswers)
            existing.Answers[k] = v;

        if (!HasPendingSurvey(existing))
            existing.SurveyCompletedAt = DateTime.UtcNow;

        return existing;
    }

    public static StoredContext FromAnalyzeResult(CompatibilityAnalysisResult result)
    {
        return new StoredContext
        {
            Version = ContextVersion,
            AnalyzedAt = result.AnalyzedAt,
            Questions = result.Questions,
            Report = result.Report,
            Answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
    }
}