using System.Text.Json;
using Delegat.Marketplace.SDK;

namespace Delegat.Services.Integration;

public sealed record IntegrationPreparedCommandPlan(
    IReadOnlyList<IntegrationStageSeed> Stages,
    IReadOnlyList<IntegrationIssue> Issues,
    bool HasBlockingIssues);

/// <summary>
/// Single host-level preparation service for V1 integration commands. Routes
/// single and bulk requests through the same adapter <see cref="IMarketplaceExecutionProvider.PrepareAsync"/>
/// call and validates stage graphs and JSON payloads before any mutation is admitted.
/// </summary>
public sealed class IntegrationPreparationService
{
    public async Task<IntegrationPreparedCommandPlan> PrepareAsync(
        IMarketplaceExecutionProvider provider,
        IntegrationPreparationInput input,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(input);

        if (input.Items.Count == 0)
        {
            return new([], [new IntegrationIssue("empty_items", "At least one item is required for preparation", Retryable: false)], true);
        }

        var result = await provider.PrepareAsync(input, ct);
        var issues = new List<IntegrationIssue>(result.Issues);
        var blockedItems = new HashSet<string>(
            result.Issues.Where(i => !string.IsNullOrWhiteSpace(i.ItemKey)).Select(i => i.ItemKey!),
            StringComparer.Ordinal);

        var seeds = new List<IntegrationStageSeed>();
        var seenItemKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in result.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemKey))
            {
                issues.Add(new IntegrationIssue("invalid_item_key", "Prepared item is missing itemKey", Retryable: false));
                continue;
            }
            if (!seenItemKeys.Add(item.ItemKey))
            {
                issues.Add(new IntegrationIssue("duplicate_item_key", $"Duplicate prepared item '{item.ItemKey}'", Retryable: false, ItemKey: item.ItemKey));
                continue;
            }
            if (blockedItems.Contains(item.ItemKey))
            {
                if (item.Stages.Any(s => s.IsEffectful))
                {
                    issues.Add(new IntegrationIssue("blocked_item_has_effectful_stages",
                        $"Blocked item '{item.ItemKey}' must not emit effectful stages", Retryable: false, ItemKey: item.ItemKey));
                }
                continue;
            }

            var stageKeys = new HashSet<string>(StringComparer.Ordinal);
            var itemSeeds = new List<IntegrationStageSeed>(item.Stages.Count);
            var itemValid = true;

            for (var i = 0; i < item.Stages.Count; i++)
            {
                var st = item.Stages[i];
                if (string.IsNullOrWhiteSpace(st.StageKey))
                {
                    issues.Add(new IntegrationIssue("invalid_stage_key", "Prepared stage is missing stageKey", Retryable: false, ItemKey: item.ItemKey));
                    itemValid = false;
                    break;
                }
                if (!stageKeys.Add(st.StageKey))
                {
                    issues.Add(new IntegrationIssue("duplicate_stage_key", $"Duplicate stageKey '{st.StageKey}' in item '{item.ItemKey}'",
                        StageKey: st.StageKey, Retryable: false, ItemKey: item.ItemKey));
                    itemValid = false;
                    break;
                }
                if (st.IsEffectful && !IsValidJsonPayload(st.PayloadJson))
                {
                    issues.Add(new IntegrationIssue("invalid_stage_payload", $"Effectful stage '{st.StageKey}' has invalid JSON payload",
                        StageKey: st.StageKey, Retryable: false, ItemKey: item.ItemKey));
                    itemValid = false;
                    break;
                }

                itemSeeds.Add(new IntegrationStageSeed(
                    ItemKey: item.ItemKey,
                    StageKey: st.StageKey,
                    Kind: st.Kind.ToString(),
                    Position: i,
                    PayloadJson: st.PayloadJson,
                    DependsOn: st.DependsOn,
                    BatchKey: st.BatchKey,
                    IsEffectful: st.IsEffectful));
            }

            if (!itemValid)
                continue;

            // Validate item-local dependency graph: all targets must exist and graph must be acyclic.
            var depMap = item.Stages.ToDictionary(s => s.StageKey, s => s.DependsOn, StringComparer.Ordinal);
            if (!ValidateDependencyGraph(item.ItemKey, depMap, issues))
                continue;

            seeds.AddRange(itemSeeds);
        }

        var hasBlocking = issues.Count > 0 || seeds.Count == 0;
        return new IntegrationPreparedCommandPlan(seeds, issues, hasBlocking);
    }

    private static bool IsValidJsonPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return false;
        try
        {
            using var _ = JsonDocument.Parse(payloadJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ValidateDependencyGraph(
        string itemKey,
        Dictionary<string, IReadOnlyList<string>> depMap,
        List<IntegrationIssue> issues)
    {
        foreach (var (stageKey, deps) in depMap)
        {
            var seenDeps = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dep in deps)
            {
                if (!seenDeps.Add(dep))
                {
                    issues.Add(new IntegrationIssue("duplicate_stage_dependency",
                        $"Stage '{stageKey}' declares duplicate dependency '{dep}'",
                        StageKey: stageKey, Retryable: false, ItemKey: itemKey));
                    return false;
                }
                if (!depMap.ContainsKey(dep))
                {
                    issues.Add(new IntegrationIssue("missing_stage_dependency",
                        $"Stage '{stageKey}' depends on unknown stage '{dep}'",
                        StageKey: stageKey, Retryable: false, ItemKey: itemKey));
                    return false;
                }
            }
        }

        // 0 = unvisited, 1 = visiting, 2 = visited
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Visit(string key)
        {
            if (state.TryGetValue(key, out var s))
                return s != 1;
            state[key] = 1;
            foreach (var dep in depMap[key])
            {
                if (!Visit(dep))
                    return false;
            }
            state[key] = 2;
            return true;
        }

        foreach (var key in depMap.Keys)
        {
            if (!Visit(key))
            {
                issues.Add(new IntegrationIssue("cyclic_stage_dependency",
                    $"Cyclic stage dependency detected in item '{itemKey}'",
                    StageKey: key, Retryable: false, ItemKey: itemKey));
                return false;
            }
        }
        return true;
    }
}
