using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace.Mapping;

/// <summary>
/// Aligns dictionary values between a local category (often imported from marketplace A)
/// and a target adapter field (marketplace B) with different option keys.
/// </summary>
public static class OptionAlignmentHelper
{
    public sealed record ParsedOption(string Value, string Label);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<ParsedOption> ParseAttributeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return [];

        try
        {
            var objects = JsonSerializer.Deserialize<List<OptionDto>>(optionsJson, JsonOpts);
            if (objects is { Count: > 0 })
            {
                return objects
                    .Select(o => new ParsedOption(
                        o.Value ?? o.Label ?? "",
                        o.Label ?? o.Value ?? ""))
                    .Where(o => !string.IsNullOrWhiteSpace(o.Value) || !string.IsNullOrWhiteSpace(o.Label))
                    .ToList();
            }
        }
        catch (JsonException) { /* fall through */ }

        try
        {
            var strings = JsonSerializer.Deserialize<List<string>>(optionsJson, JsonOpts);
            if (strings is { Count: > 0 })
                return strings.Select(s => new ParsedOption(s, s)).ToList();
        }
        catch (JsonException) { /* ignore */ }

        return [];
    }

    /// <summary>
    /// Build valueMap entries: local stored value → target adapter option value.
    /// Matches primarily by human-readable label, then by normalized value text.
    /// </summary>
    public static List<Dictionary<string, string>> BuildValueMapEntries(
        IReadOnlyList<ParsedOption> localOptions,
        IReadOnlyList<ExternalFieldOption> targetOptions)
    {
        if (localOptions.Count == 0 || targetOptions.Count == 0) return [];

        var entries = new List<Dictionary<string, string>>();
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var local in localOptions)
        {
            if (string.IsNullOrWhiteSpace(local.Value)) continue;

            var target = FindBestMatch(local, targetOptions, usedTargets);
            if (target == null) continue;

            usedTargets.Add(target.Value);
            entries.Add(new Dictionary<string, string>
            {
                ["from"] = local.Value,
                ["to"] = target.Value,
            });
        }

        return entries;
    }

    public static bool NeedsValueMap(
        IReadOnlyList<ParsedOption> localOptions,
        IReadOnlyList<ExternalFieldOption> targetOptions)
    {
        if (localOptions.Count == 0 || targetOptions.Count == 0) return false;

        var targetValues = new HashSet<string>(
            targetOptions.Select(o => o.Value),
            StringComparer.OrdinalIgnoreCase);

        // Direct value overlap — no map needed
        if (localOptions.Any(l => targetValues.Contains(l.Value)))
            return false;

        // Label overlap with different keys — map required
        return BuildValueMapEntries(localOptions, targetOptions).Count > 0;
    }

    private static ExternalFieldOption? FindBestMatch(
        ParsedOption local,
        IReadOnlyList<ExternalFieldOption> targets,
        HashSet<string> usedTargets)
    {
        ExternalFieldOption? best = null;
        var bestScore = 0;

        foreach (var t in targets)
        {
            if (usedTargets.Contains(t.Value)) continue;

            var score = MatchScore(local, t);
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return bestScore >= 80 ? best : null;
    }

    private static int MatchScore(ParsedOption local, ExternalFieldOption target)
    {
        var localLabel = Normalize(local.Label);
        var localValue = Normalize(local.Value);
        var targetLabel = Normalize(target.Label ?? "");
        var targetValue = Normalize(target.Value);

        if (!string.IsNullOrEmpty(localLabel) && localLabel == targetLabel) return 100;
        if (!string.IsNullOrEmpty(localLabel) && localLabel == targetValue) return 95;
        if (!string.IsNullOrEmpty(localValue) && localValue == targetLabel) return 90;
        if (!string.IsNullOrEmpty(localValue) && localValue == targetValue) return 85;

        if (!string.IsNullOrEmpty(localLabel) && !string.IsNullOrEmpty(targetLabel))
        {
            if (localLabel.Contains(targetLabel, StringComparison.Ordinal) ||
                targetLabel.Contains(localLabel, StringComparison.Ordinal))
                return 70;
        }

        return 0;
    }

    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Trim().ToLowerInvariant();
        t = Regex.Replace(t, @"\s+", " ");
        return t;
    }

    /// <summary>
    /// Resolve mapping keys to canonical option values (not labels).
    /// </summary>
    public static List<Dictionary<string, string>> NormalizeValueMapMappings(
        IReadOnlyList<Dictionary<string, string>> mappings,
        IReadOnlyList<ParsedOption> localOptions,
        IReadOnlyList<ExternalFieldOption> targetOptions)
    {
        if (mappings.Count == 0) return [];

        return mappings.Select(m =>
        {
            var from = ResolveOptionValue(m.GetValueOrDefault("from"), localOptions);
            var to = ResolveTargetValue(m.GetValueOrDefault("to"), targetOptions);
            return new Dictionary<string, string> { ["from"] = from, ["to"] = to };
        }).Where(m => !string.IsNullOrWhiteSpace(m["from"]) && !string.IsNullOrWhiteSpace(m["to"]))
          .ToList();
    }

    /// <summary>
    /// Merge precomputed entries into existing mappings without overwriting user pairs.
    /// </summary>
    public static List<Dictionary<string, string>> MergeValueMapMappings(
        IReadOnlyList<Dictionary<string, string>>? existing,
        IReadOnlyList<Dictionary<string, string>> additions)
    {
        var result = new List<Dictionary<string, string>>();
        var seenFrom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in existing ?? [])
        {
            if (!m.TryGetValue("from", out var from) || string.IsNullOrWhiteSpace(from)) continue;
            seenFrom.Add(from);
            result.Add(new Dictionary<string, string>
            {
                ["from"] = from,
                ["to"] = m.GetValueOrDefault("to") ?? "",
            });
        }

        foreach (var m in additions)
        {
            if (!m.TryGetValue("from", out var from) || string.IsNullOrWhiteSpace(from)) continue;
            if (seenFrom.Contains(from)) continue;
            seenFrom.Add(from);
            result.Add(new Dictionary<string, string>
            {
                ["from"] = from,
                ["to"] = m.GetValueOrDefault("to") ?? "",
            });
        }

        return result;
    }

    private static string ResolveOptionValue(string? raw, IReadOnlyList<ParsedOption> options)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var match = options.FirstOrDefault(o =>
            string.Equals(o.Value, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.Label, raw, StringComparison.OrdinalIgnoreCase));
        return match?.Value ?? raw.Trim();
    }

    private static string ResolveTargetValue(string? raw, IReadOnlyList<ExternalFieldOption> options)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var match = options.FirstOrDefault(o =>
            string.Equals(o.Value, raw, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.Label, raw, StringComparison.OrdinalIgnoreCase));
        return match?.Value ?? raw.Trim();
    }

    private sealed class OptionDto
    {
        public string? Value { get; set; }
        public string? Label { get; set; }
    }
}