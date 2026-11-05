using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Live index of cabinet actions the assistant may call. Routes come from MVC
/// (filtered by <see cref="PlatformRequestPolicy"/>); a small extras list adds
/// operator-language aliases and request bodies that reflection cannot see.
/// </summary>
public sealed class CabinetActionCatalog
{
    private static readonly Regex BinaryFileDownloadRoute = new(
        @"^/api/.+/download$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record ActionHit(
        string Method,
        string Path,
        bool Write,
        string Note,
        string Keywords);

    private readonly IActionDescriptorCollectionProvider? _actions;
    private readonly Lazy<IReadOnlyList<ActionHit>> _all;

    public CabinetActionCatalog(IActionDescriptorCollectionProvider? actions = null)
    {
        _actions = actions;
        _all = new Lazy<IReadOnlyList<ActionHit>>(Build, isThreadSafe: true);
    }

    public IReadOnlyList<ActionHit> Search(string? query, int limit = 8)
    {
        var take = Math.Clamp(limit <= 0 ? 8 : limit, 1, 20);
        var tokens = Tokens(query);
        if (tokens.Count == 0)
            return Extras.Take(take).Select(Present).ToArray();

        return _all.Value
            .Select(h => (hit: h, score: Score(h, tokens)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.hit.Path, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(x => Present(x.hit))
            .ToArray();
    }

    /// <summary>Notes the model sees, including the shared body contract.</summary>
    internal static string InstructionText() =>
        string.Join("\n", Extras.Select(h =>
        {
            var shown = Present(h);
            return shown.Method + " " + shown.Path + "\n" + shown.Note;
        }));

    internal static bool IsSearchableAction(string method, string path) =>
        PlatformRequestPolicy.IsAllowed(method, path)
        && !(method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && BinaryFileDownloadRoute.IsMatch(PlatformRequestPolicy.NormalizePath(path)));

    private static ActionHit Present(ActionHit hit)
    {
        var note = hit.Note;
        var contract = AssistantWriteContract.Match(hit.Method, hit.Path);
        if (contract != null)
        {
            var line = contract.Method + " " + contract.PathSuffix + " " + contract.Sentence;
            if (!note.Contains(line, StringComparison.Ordinal))
                note += " " + line;
        }

        var fields = AssistantBodySchema.AcceptedFields(hit.Method, hit.Path);
        if (!string.IsNullOrEmpty(fields) && !note.Contains("Accepted body fields:", StringComparison.Ordinal))
            note += " Accepted body fields: " + fields + ".";
        return hit with { Note = note };
    }

    internal static readonly ActionHit[] Extras =
    [
        new("POST", "/marketplace/connections/{id}/products/export-batch", true,
            "Local catalog → this shop (Medusa/Woo/OpenCart/Ozon/…). Body: productIds, allMatching, or where (@withGallery and the other CATALOG WHERE templates, or a sql_readonly WHERE). HTTP 200 pending = Operations. Not GET /tools, not import.",
            "export выгрузка выгрузить shop магазин marketplace маркетплейс medusa catalog products товары карточки listings refresh update обновить отправить повторно photos фото без фото where allMatching export-batch export-bulk"),
        new("POST", "/marketplace/connections/{id}/products/export-bulk", true,
            "Same as export-batch but waits in-request. Small named sets only.",
            "export-bulk выгрузка"),
        new("POST", "/marketplace/connections/{id}/products/import-unlinked", true,
            "Shop → this catalog (Catalog Connect). Optional operationId. HTTP 200 pending = success. Not POST /tools/{id}/run.",
            "import импорт unlinked catalog-connect shop магазин"),
        new("POST", "/marketplace/connections/{id}/products/import-selected", true,
            "Import chosen remote cards. Body products[] + optional operationId.",
            "import-selected import импорт"),
        new("POST", "/marketplace/connections/{id}/match/preview", true,
            "Link existing local cards to another marketplace (not the import source). Optional operationId. Then match/apply.",
            "match preview связать link connect catalogs ozon wildberries yandex"),
        new("POST", "/marketplace/connections/{id}/match/apply", true,
            "Apply match preview items (productId, externalProductId, externalCategoryId). mappingsCreated 0 → match/repair-mappings.",
            "match apply связать"),
        new("POST", "/marketplace/connections/{id}/match/repair-mappings", true,
            "Create missing category mappings and copy characteristics after apply.",
            "repair-mappings mapping характеристики"),
        new("PATCH", "/products/bulk", true,
            "Raise or set catalog prices. Body: allMatching+wait+updates.priceDelta (add ₽ to every card), or productIds+updates.price. Then POST /tools/export-all-prices/run { connectionId } per shop. Not GET …/prices.",
            "цены price цены bulk массовое повышение наценка +500 рублей priceDelta allMatching wait ozon wildberries yandex яндекс вб озон"),
        new("POST", "/tools/export-all-prices/run", true,
            "Push local catalog prices to a shop. Body: { connectionId }. Omit connectionId = every active connection that can export prices (Ozon, Wildberries, Yandex, …). After a catalog price change. HTTP 200 = Operations.",
            "обновить цены маркетплейс магазин shop export-all-prices выгрузить цены ozon wildberries yandexmarket яндекс вб озон"),
        new("GET", "/tools", false,
            "Job-tool catalog (export all prices, scrape). Not product export to a shop.",
            "tools инструменты цены prices jobs"),
        new("PATCH", "/products/{id}", true,
            "attributeValues replaces the whole list (GET the card first).",
            "patch product characteristics характеристики attributeValues"),
        new("PATCH", "/products/{id}/group", true,
            "Move a card into a group (groupId null removes).",
            "group группа productgroups"),
        new("POST", "/products/{id}/files", true,
            "Staged chat files onto a product. Body attachmentIds.",
            "files photos фото вложения attachmentIds"),
        new("GET", "/marketplace/mapping-graph/blocks", false,
            "Canvas block catalog: node types, input/output handle ids, pin types, default width/height for layout.",
            "mapping blocks блоки размеры pins пины handles входы выходы types типы холст graph layout"),
        new("GET", "/marketplace/connections/{id}/mappings/{mappingId}/graph", false,
            "Mapping canvas JSON plus the same block catalog (types, handles, sizes). Collection title for Medusa is group_name.",
            "mapping graph холст соответствие blocks блоки medusa collections группы название выгрузки"),
        new("PUT", "/marketplace/connections/{id}/mappings/{mappingId}/graph", true,
            "Replace the whole mapping canvas. Prefer graph/connect and graph/modifiers to add blocks.",
            "mapping graph save холст"),
        new("POST", "/marketplace/connections/{id}/mappings/{mappingId}/graph/group-titles", true,
            "Shop collection titles only. String switch on Merge into card (__group__). Body: mappings [{from, to}]. PIM group names stay. Not local attribute groups. Not valueId.",
            "group-titles merge into card объединить карточку __group__ коллекции medusa славянская мифология star wars valueMap switch"),
        new("GET", "/ai/store-settings", false,
            "Personal settings for this store: store description, communication style, brevity, emoji. Read before inferring mapping constants.",
            "персональные настройки описание магазина store description стиль краткость emoji"),
        new("POST", "/marketplace/connections/{id}/mappings/{mappingId}/graph/connect", true,
            "Wire one marketplace field. Body: adapterFieldKey, sourceType attribute|productField|groupName|static, sourceAttributeId or sourceField or staticValue, sink price for the price block. Use this to finish a mapping. Not group-titles. staticValue sets a constant.",
            "mapping connect доделать маппинг связать поля характеристики основные параметры вложенные параметры attribute productField sourceAttributeId staticValue бренд материал"),
        new("POST", "/marketplace/connections/{id}/mappings/{mappingId}/graph/modifiers", true,
            "Insert a canvas modifier on that sink. Body requires adapterFieldKey (the field key, such as name or description) and modifierType (divide|multiply|round|template|valueMap|replace|regexReplace|regexExtract|…). regexReplace and regexExtract put pattern, replacement, and ignoreCase inside params. Beside modifierType those fields used to save an empty block. The engine is .NET: \\u{…} is invalid and the preview keeps the text. To strip every emoji set params.pattern to [\\uD83C-\\uD83E][\\uDC00-\\uDFFF]|[\\u2600-\\u27BF]|[\\u2B00-\\u2BFF]|\\uFE0F|\\u200D|\\u20E3, params.replacement empty, and removeModifierTypes [\"replace\"]. To keep only Latin, Cyrillic, digits, and ordinary punctuation set removeModifierTypes [\"regexReplace\", \"replace\"], params.replacement empty, and params.pattern [^0-9A-Za-z\\u0400-\\u04FF\\r\\n .,;:!?()\\[\\]{}«»\"'…\\-+–—=%№@#&*/|_~$^<>]. That replaces the previous regex. There is no descriptionCleanup field. Do not PATCH the graph. Do not export unless asked. divide params.divisor 10 is mm to cm, 1000 is g to kg. paramSources item: param divisor or factor, sourceType attribute|productField|static. valueMap params.mappings is [{from,to},…]. One call per field. One graph GET then propose_plan — do not reread the graph with a script.",
            "mapping modifier блок divide multiply единицы измерения мм см масштаб valueMap replace regexReplace regex эмодзи описание adapterFieldKey"),
        new("POST", "/marketplace/connections/{id}/resolve-mappings", false,
            "Which marketplace-category mapping each product uses on this connection, and the other mappings of the same local category. Body: { productIds }. Read this before reassigning. Does not edit the canvas.",
            "resolve-mappings переназначить маппинг товару categoryMappingId availableMappings selectedMappingId фигурки две категории wildberries"),
        new("POST", "/marketplace/connections/{id}/save-mapping-selections", true,
            "Assign each product to one category mapping on this connection. Body: { selections: [{ productId, categoryMappingId }] }. categoryMappingId must be one of that product’s availableMappings from resolve-mappings. Use when one local category exports to several marketplace categories. Does not edit the canvas and does not create a mapping.",
            "save-mapping-selections переназначить назначить маппинг товару categoryMappingId фигурки wildberries другая категория"),
    ];

    private IReadOnlyList<ActionHit> Build()
    {
        var list = new List<ActionHit>(Extras);
        if (_actions == null)
            return list;

        foreach (var d in _actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            var ns = d.ControllerTypeInfo.Namespace ?? "";
            if (ns.Contains("PublicApi", StringComparison.Ordinal))
                continue;

            var template = CombinedTemplate(d);
            if (string.IsNullOrEmpty(template))
                continue;

            var httpMethods = d.ActionConstraints?
                    .OfType<HttpMethodActionConstraint>()
                    .SelectMany(c => c.HttpMethods)
                    .ToList()
                ?? [];
            if (httpMethods.Count == 0)
            {
                httpMethods = d.EndpointMetadata?
                    .OfType<HttpMethodMetadata>()
                    .SelectMany(m => m.HttpMethods)
                    .ToList() ?? [];
            }

            foreach (var method in httpMethods.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsSearchableAction(method, template))
                    continue;
                var path = PlatformRequestPolicy.NormalizePath(template);
                if (list.Any(h => h.Method.Equals(method, StringComparison.OrdinalIgnoreCase)
                    && PathsEqual(h.Path, path)))
                    continue;

                var words = SplitName(d.ActionName) + " " + SplitName(d.ControllerName);
                list.Add(new ActionHit(
                    method.ToUpperInvariant(),
                    path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ? path["/api".Length..] : path,
                    PlatformRequestPolicy.IsMutating(method),
                    words.Trim(),
                    words));
            }
        }

        return list;
    }

    private static string CombinedTemplate(ControllerActionDescriptor d)
    {
        var t = d.AttributeRouteInfo?.Template;
        if (!string.IsNullOrEmpty(t))
            return t.StartsWith('/') ? t : "/" + t;
        return "";
    }

    private static bool PathsEqual(string a, string b)
    {
        var na = PlatformRequestPolicy.NormalizePath(a);
        var nb = PlatformRequestPolicy.NormalizePath(b);
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static int Score(ActionHit hit, IReadOnlyList<string> tokens)
    {
        // Search the action name and curated aliases, not the body/usage notes: a long
        // note can mention unrelated actions and drown out the actual route.
        // Route parameters such as {id} are not evidence of what an action does.
        var pathWords = Tokens(Regex.Replace(hit.Path, @"\{[^}]+\}", ""));
        var keywordWords = Tokens(hit.Keywords);
        var score = 0;
        foreach (var t in tokens)
        {
            if (pathWords.Contains(t))
                score += 5;
            else if (keywordWords.Contains(t))
                score += 3;
            else if (pathWords.Any(word => RelatedWord(t, word)))
                score += 3;
            else if (keywordWords.Any(word => RelatedWord(t, word)))
                score += 2;
        }

        return score;
    }

    private static bool RelatedWord(string query, string word) =>
        Math.Min(query.Length, word.Length) >= 5
        && (query.StartsWith(word, StringComparison.Ordinal)
            || word.StartsWith(query, StringComparison.Ordinal)
            || query.AsSpan(0, 5).SequenceEqual(word.AsSpan(0, 5)));

    private static List<string> Tokens(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : Regex.Split(query.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}]+")
                .Where(t => t.Length >= 2)
                .Distinct()
                .ToList();

    private static string SplitName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
    }
}
