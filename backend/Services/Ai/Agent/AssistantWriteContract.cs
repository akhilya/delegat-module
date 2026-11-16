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
        "Body { productIds?, allMatching?, where?, wait?, updates }. Use exact productIds for a previously selected set, never allMatching. Supported updates: name, description, storageQuantity, price, oldPrice, minPrice, netPrice, currency, barcode, generateBarcodes, priceDelta, attributeValues, storeAttributeValues. One updates object applies to every selected product; it is NOT a per-product values map. updates.priceDelta adds to each price (including sized variants with their own price); do not combine price with priceDelta. Attribute lists upsert the specified values, unlike single-product PATCH which replaces the lists; unique attribute values cannot be shared across products. updates.generateBarcodes=true fills only missing barcodes. To regenerate ALL selected product barcodes in one request, send updates { barcode: \"\", generateBarcodes: true }; fields are cleared then unique EAN-13 values are generated server-side, reserving existing product and variant barcodes including trash. Do not compute random barcodes in Python for this built-in action. A nonempty updates.barcode sets ONE exact value and is invalid for multiple products; distinct externally supplied barcodes need individual PATCH requests. wait=true returns updatedCount/failedCount; otherwise a background operation is queued. Bulk can partially apply: do not claim atomicity or completion from enqueue alone.";

    public const string MatchApply =
        "Body { items: [{ productId, externalProductId, externalCategoryId?, externalCategoryName? }], operationId? }.";

    public const string ExportPrices =
        "Body { connectionId? }.";

    public const string StoreFileFromText =
        "Body { fileName, text, contentType?, folderId? }. Creates one private UTF-8 store file; text is limited to 262144 UTF-8 bytes. contentType is text/plain (default), text/csv, or application/json. Returns id, fileName, length, and an authenticated downloadUrl for the selected store. Requires files.manage and a confirmed write plan. Use only for small text already available; never invent an upload route or put binary/base64 content here. Large or sandbox-generated files need the separate planned HTTP artifact handoff.";

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
        new("POST", "store-storage/files/from-text", StoreFileFromText,
            ["fileName", "text", "contentType", "folderId"],
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
