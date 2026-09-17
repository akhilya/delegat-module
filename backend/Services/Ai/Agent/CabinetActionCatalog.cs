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
        {
            return Extras.Take(take).ToArray();
        }

        return _all.Value
            .Select(h => (hit: h, score: Score(h, tokens)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.hit.Path, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(x => x.hit)
            .ToArray();
    }

    internal static readonly ActionHit[] Extras =
    [
        new("POST", "/marketplace/connections/{id}/products/export-batch", true,
            "Local catalog → this shop (Medusa/Woo/OpenCart/Ozon/…). Body: productIds, or allMatching+skipWithoutImages (gallery photos). HTTP 200 pending = Operations. Not GET /tools, not import.",
            "export выгрузка выгрузить shop магазин medusa catalog products photos фото без фото allMatching skipWithoutImages export-batch export-bulk"),
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
        new("POST", "/marketplace/connections/{id}/mappings/{mappingId}/graph/connect", true,
            "Add a source block onto a sink. Body: adapterFieldKey, sourceType (groupName|productField|attribute|static), sourceField?, sourceAttributeId?. Medusa collection title: group_name + groupName. Does not rename local groups.",
            "mapping connect block блок groupName group_name collection название группы medusa выгрузка valueMap"),
        new("POST", "/marketplace/connections/{id}/mappings/{mappingId}/graph/modifiers", true,
            "Insert a canvas modifier on that sink. Body: adapterFieldKey, modifierType (valueMap|replace|…), params. valueMap params.mappings is [{from,to},…]. Call after graph/connect. Local group names stay.",
            "mapping modifier блок valueMap replace group_name medusa collections переименовать выгрузка"),
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
                if (!PlatformRequestPolicy.IsAllowed(method, template))
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
        var hay = $"{hit.Method} {hit.Path} {hit.Note} {hit.Keywords}".ToLowerInvariant();
        var score = 0;
        foreach (var t in tokens)
        {
            if (t.Length < 2) continue;
            if (hit.Path.Contains(t, StringComparison.OrdinalIgnoreCase))
                score += 5;
            else if (hay.Contains(t, StringComparison.Ordinal))
                score += 2;
        }

        return score;
    }

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
