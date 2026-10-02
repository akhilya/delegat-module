namespace Delegat.Services.Ai.Agent;

/// <summary>
/// The one body sentence for each cabinet write the assistant is taught.
/// Prompt, platform_request surface, and search_actions all embed these strings.
/// A sketch that names a field the action does not read, or that puts a params
/// value beside the modifier, fails <see cref="AssistantInstructionAudit"/>.
/// </summary>
public static class AssistantWriteContract
{
    public sealed record Entry(
        string Method,
        string PathSuffix,
        string Sentence,
        string[] RootKeys,
        string[] BelongsInParams);

    public const string GraphModifiers =
        "Body { adapterFieldKey, modifierType, params, sink?, removeModifierTypes?, paramSources? }. regexReplace and regexExtract require params.pattern inside params, together with params.replacement and params.ignoreCase. divide reads params.divisor, multiply reads params.factor, valueMap reads params.mappings. pattern, replacement, divisor, or factor beside modifierType is rejected.";

    public const string GraphConnect =
        "Body { adapterFieldKey, sourceType, sourceAttributeId?, sourceField?, staticValue?, sink? }.";

    public const string GroupTitles =
        "Body { mappings: [{ from, to }] }.";

    public const string SaveSelections =
        "Body { selections: [{ productId, categoryMappingId }] }.";

    public const string ResolveMappings =
        "Body { productIds }.";

    public const string ExportBatch =
        "Body { productIds?, allMatching?, where? }.";

    public const string ProductsBulk =
        "Body { productIds?, allMatching?, where?, wait?, updates }. updates.priceDelta adds to each price.";

    public const string MatchApply =
        "Body { items: [{ productId, externalProductId, externalCategoryId?, externalCategoryName? }], operationId? }.";

    public const string ExportPrices =
        "Body { connectionId? }.";

    public static readonly Entry[] All =
    [
        new("POST", "graph/modifiers", GraphModifiers,
            ["adapterFieldKey", "modifierType", "params", "sink", "removeModifierTypes", "paramSources"],
            ["pattern", "replacement", "ignoreCase", "group", "divisor", "factor", "mappings", "search", "template"]),
        new("POST", "graph/connect", GraphConnect,
            ["adapterFieldKey", "sourceType", "sourceAttributeId", "sourceField", "staticValue", "sink"],
            []),
        new("POST", "graph/group-titles", GroupTitles,
            ["mappings"],
            []),
        new("POST", "save-mapping-selections", SaveSelections,
            ["selections"],
            []),
        new("POST", "resolve-mappings", ResolveMappings,
            ["productIds"],
            []),
        new("POST", "products/export-batch", ExportBatch,
            ["productIds", "allMatching", "where"],
            []),
        new("PATCH", "products/bulk", ProductsBulk,
            ["productIds", "allMatching", "where", "wait", "updates"],
            []),
        new("POST", "match/apply", MatchApply,
            ["items", "operationId"],
            []),
        new("POST", "export-all-prices/run", ExportPrices,
            ["connectionId"],
            []),
    ];

    public static string AllText => string.Join("\n", All.Select(e => e.Method + " " + e.PathSuffix + " " + e.Sentence));

    public static string For(params string[] pathSuffixes) =>
        string.Join("\n", All.Where(e => pathSuffixes.Any(s =>
                e.PathSuffix.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Method + " " + e.PathSuffix + " " + e.Sentence));

    public static Entry? Match(string? method, string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        Entry? best = null;
        foreach (var entry in All)
        {
            if (!PathHas(path, entry.PathSuffix))
                continue;
            if (method != null && !method.Equals(entry.Method, StringComparison.OrdinalIgnoreCase))
                continue;
            if (best == null || entry.PathSuffix.Length > best.PathSuffix.Length)
                best = entry;
        }
        return best;
    }

    public static bool PathHas(string path, string suffix)
    {
        var norm = path.Replace('\\', '/');
        return norm.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || norm.Contains("/" + suffix, StringComparison.OrdinalIgnoreCase);
    }
}
