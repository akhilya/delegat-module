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
        string Keywords,
        string Capabilities = "");

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
        var action = AssistantBodySchema.Match(hit.Method, hit.Path);
        if (action?.Body.Properties.TryGetValue("updates", out var updates) == true)
            note += " Accepted updates fields (DTO shape; use the supported semantics above): "
                + string.Join(", ", updates.Properties.Keys.OrderBy(k => k, StringComparer.Ordinal)) + ".";
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
            "Built-in bulk catalog editing, including server-generated unique barcodes, common scalar fields, and attribute upserts. Prefer one request for a selected set when supported; no Python preparation for built-in generation. This changes the local catalog only; marketplace export is separate and requires user intent.",
            "цены price bulk массовое массовый выбранных товары карточки редактирование изменить обновить повышение наценка +500 рублей priceDelta allMatching wait ozon wildberries yandex яндекс вб озон generate regenerate перегенерировать пересоздать сгенерировать заполнить заменить"),
        new("POST", "/tools/export-all-prices/run", true,
            "Push local catalog prices to a shop. Body: { connectionId }. Omit connectionId = every active connection that can export prices (Ozon, Wildberries, Yandex, …). After a catalog price change. HTTP 200 = Operations.",
            "обновить цены маркетплейс магазин shop export-all-prices выгрузить цены ozon wildberries yandexmarket яндекс вб озон",
            "price prices цена цены"),
        new("GET", "/tools", false,
            "Job-tool catalog (export all prices, scrape). Not product export to a shop.",
            "tools инструменты цены prices jobs"),
        new("PATCH", "/products/{id}", true,
            "attributeValues replaces the whole list (GET the card first).",
            "patch product characteristics характеристики attributeValues"),
        new("GET", "/categories/{categoryId}/attributes/summary", false,
            "Lightweight category attribute definitions, including unitCode, without option dictionaries or editor payloads. Prefer this when reading characteristic units.",
            "категория категории категорий характеристики характеристика атрибут атрибуты единица единицу единицы измерения измерений unit units measurement summary"),
        new("GET", "/store-attributes/summary", false,
            "Lightweight store-wide attribute definitions, including unitCode, without option dictionaries or editor payloads. Prefer this when reading characteristic units.",
            "global store-wide глобальный глобальная глобальные глобальных общая общие общих общими характеристики атрибуты свойства единица единицу единицы измерения измерений unit units measurement summary"),
        new("PATCH", "/categories/{categoryId}/attributes/{attributeId}", true,
            "Update one category attribute definition, including unitCode. Changing unit metadata does not convert stored product values.",
            "категория категории категорий характеристика характеристики атрибут атрибуты единица единицу единицы измерения измерений unit units measurement"),
        new("PATCH", "/store-attributes/{attributeId}", true,
            "Update one store-wide attribute definition, including unitCode. Changing unit metadata does not convert stored product values.",
            "global store-wide глобальный глобальная глобальные глобальных общая общие общих общими характеристики атрибуты свойства единица единицу единицы измерения измерений unit units measurement"),
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
        new("POST", "/store-storage/files/from-text", true,
            "Save small text that is already generated as one private store file (CSV/JSON/TXT up to 262144 UTF-8 bytes). Returns an authenticated downloadUrl to share in chat. Not for binary or large artifacts.",
            "csv file файл файлы save сохранить storage хранилище download скачать generated сгенерированный text текст upload загрузить from-text store-storage"),
        new("POST", "/products/{productId}/files/from-text", true,
            "Save small text already generated as one private product file, not store storage (CSV/JSON/TXT up to 262144 UTF-8 bytes). folderId belongs to this product. Returns productId and an authenticated downloadUrl. Not for binary or large artifacts.",
            "product товар товара карточка productId csv file файл файлы save сохранить generated сгенерированный text текст upload загрузить from-text download скачать"),
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
        var fieldWords = AssistantBodySchema.UpdateFields(hit.Method, hit.Path)
            .SelectMany(field => Tokens(field + " " + FieldAliases.GetValueOrDefault(field)))
            .Concat(Tokens(hit.Capabilities))
            .Distinct().ToList();
        var score = 0;
        var attributeUnitAction = hit.Path.Contains("/attributes", StringComparison.OrdinalIgnoreCase)
            || hit.Path.Contains("store-attributes", StringComparison.OrdinalIgnoreCase);
        var readIntent = tokens.Any(ReadIntentWords.Contains);
        var writeIntent = tokens.Any(WriteIntentWords.Contains);
        foreach (var t in tokens)
        {
            // Concrete DTO fields and their translations outweigh generic verbs
            // such as "update", without indexing unrelated prose in usage notes.
            if (fieldWords.Contains(t))
                score += 12;
            else if (fieldWords.Any(word => RelatedWord(t, word)))
                score += 9;
            else if (pathWords.Contains(t))
                score += 5;
            else if (keywordWords.Contains(t))
                score += 3;
            else if (pathWords.Any(word => RelatedWord(t, word)))
                score += 3;
            else if (keywordWords.Any(word => RelatedWord(t, word)))
                score += 2;
        }

        if (score > 0 && attributeUnitAction
            && hit.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && (readIntent || !writeIntent))
            score += 20;
        if (score > 0 && attributeUnitAction && PlatformRequestPolicy.IsMutating(hit.Method) && writeIntent)
            score += 20;

        return score;
    }

    private static readonly IReadOnlyDictionary<string, string> FieldAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["barcode"] = "barcode barcodes EAN штрихкод штрихкоды штрих код",
        ["generateBarcodes"] = "regenerate generate barcode barcodes перегенерировать сгенерировать штрихкоды",
        ["name"] = "name title название наименование",
        ["description"] = "description описание",
        ["storageQuantity"] = "stock quantity остаток остатки количество запас",
        ["price"] = "price цена цены",
        ["oldPrice"] = "old price старая цена",
        ["currency"] = "currency валюта",
        ["attributeValues"] = "attributes characteristics характеристики атрибуты",
        ["storeAttributeValues"] = "global attributes глобальные характеристики",
        ["unitCode"] = "unit units measurement unitcode единица единицу единицы измерения измерений ед измерения код единицы",
    };

    private static readonly HashSet<string> ReadIntentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "show", "read", "lookup", "view", "get", "see", "display", "list", "check",
        "посмотреть", "покажи", "показать", "прочитать", "узнать", "список", "проверь", "найти",
    };

    private static readonly HashSet<string> WriteIntentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "change", "update", "edit", "patch", "set", "modify", "alter",
        "измени", "изменить", "обнови", "обновить", "поменяй", "поменять",
        "установи", "установить", "поставь", "поставить", "задать", "задай", "сменить",
    };

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
