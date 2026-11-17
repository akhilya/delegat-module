using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// One structural capability descriptor per cabinet action, built once from MVC
/// action descriptors plus the curated <see cref="CabinetActionCatalog.Extras"/>.
/// Serves preflight validation (<see cref="AgentPlatformGateway"/>), discovery
/// callability flags (<see cref="CabinetActionCatalog"/>), and the propose_plan
/// feasibility gate — a route that cannot work never reaches the invoker.
/// </summary>
public sealed class CabinetCapabilityIndex
{
    public const string StatusOk = "ok";
    public const string StatusRouteDenied = "route_denied";
    public const string StatusRouteNotFound = "route_not_found";
    public const string StatusMethodNotAllowed = "method_not_allowed";
    public const string StatusMultipartRequired = "multipart_required";
    public const string StatusBinaryDownload = "binary_download";

    public const string ClassForbidden = "forbidden";
    public const string ClassRouteNotFound = "route_not_found";
    public const string ClassMethodNotAllowed = "method_not_allowed";
    public const string ClassTransportMismatch = "transport_mismatch";

    public const int MaxAlternatives = 3;

    private static readonly Regex BinaryFileDownloadRoute = new(
        @"^/api/.+/download$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>One callable or known-non-callable cabinet route.</summary>
    /// <param name="Method">HTTP method, upper case.</param>
    /// <param name="PathTemplate">Normalized route template under /api.</param>
    /// <param name="Write">Whether the method mutates state.</param>
    /// <param name="JsonCallable">Whether the JSON platform_request invoker can perform the call.</param>
    /// <param name="RequiresMultipart">The action consumes a multipart/form body (parameter or hand-parsed).</param>
    /// <param name="BinaryResponse">The action streams a binary download.</param>
    /// <param name="Denied"><see cref="PlatformRequestPolicy"/> denies the route for the assistant.</param>
    /// <param name="SearchText">Action/controller name words used by discovery.</param>
    /// <param name="FromExtras">The descriptor comes from the curated extras list, not MVC.</param>
    public sealed record Capability(
        string Method,
        string PathTemplate,
        bool Write,
        bool JsonCallable,
        bool RequiresMultipart,
        bool BinaryResponse,
        bool Denied,
        string SearchText = "",
        bool FromExtras = false)
    {
        public string Transport =>
            RequiresMultipart ? "multipart" : BinaryResponse ? "binary" : "json";
    }

    /// <summary>A validated callable route offered as a correction.</summary>
    public sealed record Alternative(string Method, string Path);

    /// <summary>Result of checking one method/path against the index.</summary>
    public sealed record Validation(
        string Status,
        string Method,
        string Path,
        Capability? MatchedCapability,
        IReadOnlyList<Alternative> Alternatives,
        IReadOnlyList<string> AllowedMethods,
        string Details)
    {
        /// <summary>Machine-readable error class for the structured tool error.</summary>
        public string ErrorClass => Status switch
        {
            StatusRouteDenied => ClassForbidden,
            StatusRouteNotFound => ClassRouteNotFound,
            StatusMethodNotAllowed => ClassMethodNotAllowed,
            StatusMultipartRequired or StatusBinaryDownload => ClassTransportMismatch,
            _ => "",
        };
    }

    private readonly IActionDescriptorCollectionProvider? _actions;
    private readonly Lazy<IndexData> _data;

    public CabinetCapabilityIndex(IActionDescriptorCollectionProvider? actions = null)
    {
        _actions = actions;
        _data = new Lazy<IndexData>(Build, isThreadSafe: true);
    }

    /// <summary>Every capability, extras included, in deterministic order.</summary>
    public IReadOnlyList<Capability> Capabilities => _data.Value.All;

    /// <summary>Capabilities derived from MVC action descriptors only.</summary>
    public IReadOnlyList<Capability> MvcCapabilities => _data.Value.Mvc;

    /// <summary>False when no MVC descriptors were indexed; validation degrades to ok.</summary>
    public bool HasMvcRoutes => _data.Value.Mvc.Count > 0;

    private sealed record IndexedCapability(Capability Capability, string[] Segments, TemplateSegment[] Parsed);

    /// <summary>
    /// One parsed template segment: a literal, or a parameter with an optional
    /// route constraint (int/guid/…) or a catch-all marker.
    /// </summary>
    private readonly record struct TemplateSegment(bool IsParameter, string Literal, string? Constraint, bool CatchAll);

    private sealed record IndexData(
        IReadOnlyList<Capability> All,
        IReadOnlyList<Capability> Mvc,
        IReadOnlyList<IndexedCapability> Indexed);

    /// <summary>
    /// Checks whether the JSON invoker could perform this call. An empty index
    /// (no MVC descriptors wired) degrades to <see cref="StatusOk"/> so tests and
    /// surfaces without the action catalog are not rejected by guesswork.
    /// </summary>
    public Validation Validate(string method, string rawPath)
    {
        var upper = (method ?? "GET").Trim().ToUpperInvariant();
        var apiPath = PlatformRequestPolicy.NormalizePath(rawPath ?? "");

        if (PlatformRequestPolicy.DenyReason(upper, apiPath) != null)
        {
            return Reject(StatusRouteDenied, upper, apiPath, null,
                "This route is denied for the assistant.");
        }

        var data = _data.Value;
        if (data.Mvc.Count == 0)
            return new Validation(StatusOk, upper, apiPath, null, [], [], "");

        var segments = PathSegments(apiPath);
        var scored = data.Indexed
            .Select(c => (item: c, key: MatchKey(c.Parsed, segments)))
            .Where(x => x.key != null)
            .ToList();
        if (scored.Count == 0)
        {
            return Reject(StatusRouteNotFound, upper, apiPath, null,
                "No cabinet action matches this method and path. "
                + "Pick a listed callable alternative or discover routes with search_actions.",
                segments);
        }

        // ASP.NET picks the most specific matching template (literal segments
        // beat parameters, constrained beat unconstrained, catch-all is last)
        // before checking verbs — so a literal sibling route wins over a
        // parameter route even when the parameter could bind the same text.
        var bestKey = scored.Max(x => x.key);
        var templateMatches = scored.Where(x => x.key == bestKey).Select(x => x.item).ToList();

        var methodMatches = templateMatches
            .Where(c => c.Capability.Method.Equals(upper, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (methodMatches.Count == 0)
        {
            var allowed = templateMatches
                .Select(c => c.Capability.Method)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToArray();
            return new Validation(
                StatusMethodNotAllowed,
                upper,
                apiPath,
                null,
                Alternatives(segments),
                allowed,
                $"This route exists for {string.Join("/", allowed)} but not {upper}.");
        }

        var capability = methodMatches[0].Capability;
        if (capability.RequiresMultipart)
        {
            return Reject(StatusMultipartRequired, upper, apiPath, capability,
                "This action requires multipart/form-data; platform_request sends JSON only.",
                segments);
        }
        if (capability.BinaryResponse)
        {
            return Reject(StatusBinaryDownload, upper, apiPath, capability,
                "This route returns a binary download; call create_file_download_link "
                + "with the file id and GET its short-lived downloadUrl instead.",
                segments);
        }
        if (capability.Denied)
        {
            return Reject(StatusRouteDenied, upper, apiPath, capability,
                "This route is denied for the assistant.",
                segments);
        }

        return new Validation(StatusOk, upper, apiPath, capability, [], [], "");
    }

    /// <summary>
    /// Plan feasibility gate: the first platform_request step that cannot run is
    /// reported with the verified cause and the callable alternatives, so the
    /// plan card never promises a known-unexecutable call. Non-platform steps
    /// and an empty index are untouched.
    /// </summary>
    public string? PlanStepRejection(IReadOnlyList<JsonElement> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.ValueKind != JsonValueKind.Object)
                continue;
            var tool = step.TryGetProperty("tool", out var toolEl)
                && toolEl.ValueKind == JsonValueKind.String
                    ? toolEl.GetString() ?? ""
                    : "";
            if (tool.StartsWith("functions.", StringComparison.OrdinalIgnoreCase))
                tool = tool["functions.".Length..];
            if (!tool.Equals("platform_request", StringComparison.OrdinalIgnoreCase))
                continue;

            var method = "GET";
            var path = "";
            if (step.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
            {
                if (args.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String)
                    method = m.GetString() ?? "GET";
                if (args.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                    path = p.GetString() ?? "";
            }

            var validation = Validate(method, path);
            if (validation.Status == StatusOk)
                continue;

            var shownPath = string.IsNullOrWhiteSpace(path) ? "(no path)" : path.Trim();
            var alternatives = validation.Alternatives.Count > 0
                ? " Callable alternatives: "
                    + string.Join(", ", validation.Alternatives.Select(a => a.Method + " " + a.Path)) + "."
                : "";
            return $"Plan not presented: step {i + 1} calls {method.ToUpperInvariant()} {shownPath} which "
                + PlanReason(validation) + "." + alternatives
                + " Correct the step to a callable route or drop it, then call propose_plan again by itself.";
        }
        return null;
    }

    private static string PlanReason(Validation validation) => validation.Status switch
    {
        StatusMultipartRequired => "requires multipart — platform_request is JSON-only",
        StatusBinaryDownload => "returns a binary download — not a JSON call",
        StatusMethodNotAllowed =>
            $"exists only for {string.Join("/", validation.AllowedMethods)}",
        StatusRouteDenied => "is denied for the assistant",
        _ => "is not a real cabinet route",
    };

    private Validation Reject(
        string status, string method, string apiPath, Capability? matched, string details, string[]? segments = null) =>
        new(status, method, apiPath, matched, Alternatives(segments ?? PathSegments(apiPath)), [], details);

    private IReadOnlyList<Alternative> Alternatives(string[] pathSegments)
    {
        return _data.Value.Indexed
            .Where(c => c.Capability.JsonCallable && !c.Capability.Denied)
            .Select(c => (cap: c.Capability, prefix: LiteralPrefix(c.Segments, pathSegments),
                terminal: SameTerminalLiteral(c.Segments, pathSegments)))
            .Where(x => x.prefix > 1 || x.terminal)
            .OrderByDescending(x => x.prefix)
            .ThenByDescending(x => x.terminal)
            .ThenBy(x => x.cap.Method, StringComparer.Ordinal)
            .ThenBy(x => x.cap.PathTemplate, StringComparer.Ordinal)
            .Take(MaxAlternatives)
            .Select(x => new Alternative(x.cap.Method, DisplayPath(x.cap.PathTemplate)))
            .ToArray();
    }

    private IndexData Build()
    {
        var mvc = new List<Capability>();
        var indexed = new List<IndexedCapability>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hit in CabinetActionCatalog.Extras)
        {
            var template = PlatformRequestPolicy.NormalizePath(hit.Path);
            var capability = new Capability(
                hit.Method.ToUpperInvariant(),
                template,
                hit.Write,
                JsonCallable: true,
                RequiresMultipart: false,
                BinaryResponse: false,
                Denied: PlatformRequestPolicy.DenyReason(hit.Method, template) != null,
                FromExtras: true);
            if (seen.Add(Key(capability)))
                indexed.Add(new IndexedCapability(capability, PathSegments(template), ParseTemplate(template)));
        }

        if (_actions == null)
            return new IndexData([.. mvc, .. indexed.Select(i => i.Capability)], mvc, indexed);

        foreach (var d in _actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            var ns = d.ControllerTypeInfo?.Namespace ?? "";
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

            var normalized = PlatformRequestPolicy.NormalizePath(template);
            var requiresMultipart = ConsumesMultipart(d);
            var searchText = (SplitName(d.ActionName) + " " + SplitName(d.ControllerName)).Trim();

            foreach (var method in httpMethods.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var upper = method.ToUpperInvariant();
                var binary = HttpMethods.IsGet(upper) && BinaryFileDownloadRoute.IsMatch(normalized);
                var capability = new Capability(
                    upper,
                    normalized,
                    PlatformRequestPolicy.IsMutating(upper),
                    JsonCallable: !requiresMultipart && !binary,
                    RequiresMultipart: requiresMultipart,
                    BinaryResponse: binary,
                    Denied: PlatformRequestPolicy.DenyReason(upper, normalized) != null,
                    SearchText: searchText);
                if (!seen.Add(Key(capability)))
                    continue;
                mvc.Add(capability);
                indexed.Add(new IndexedCapability(capability, PathSegments(normalized), ParseTemplate(normalized)));
            }
        }

        return new IndexData([.. mvc, .. indexed.Select(i => i.Capability)], mvc, indexed);
    }

    private static string Key(Capability c) => c.Method + " " + c.PathTemplate;

    /// <summary>Path the model should write: no forced /api prefix.</summary>
    internal static string DisplayPath(string normalizedTemplate) =>
        normalizedTemplate.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            ? normalizedTemplate["/api".Length..]
            : normalizedTemplate;

    private static string CombinedTemplate(ControllerActionDescriptor d)
    {
        var t = d.AttributeRouteInfo?.Template;
        if (string.IsNullOrEmpty(t))
            return "";
        return t.StartsWith('/') ? t : "/" + t;
    }

    /// <summary>
    /// Multipart detection: form-file bound parameters, a declared
    /// multipart/form-data consumption, or the hand-parsed body filter used by
    /// the streaming uploads. An action that only reads the form body in a
    /// helper with no marker still classifies as JSON — a recorded limit.
    /// </summary>
    private static bool ConsumesMultipart(ControllerActionDescriptor d)
    {
        if (d.Parameters is { Count: > 0 } parameters)
        {
            foreach (var p in parameters)
            {
                var source = p.BindingInfo?.BindingSource;
                if (source == BindingSource.FormFile || source == BindingSource.Form)
                    return true;
                if (IsFormFileType(p.ParameterType))
                    return true;
            }
        }

        if (ConsumesMultipartMetadata(d.ActionConstraints)
            || ConsumesMultipartMetadata(d.EndpointMetadata))
            return true;

        if (d.FilterDescriptors != null)
        {
            foreach (var filter in d.FilterDescriptors)
            {
                if (filter.Filter?.GetType().Name.Contains("DisableFormValueModelBinding", StringComparison.Ordinal) == true)
                    return true;
            }
        }

        return false;
    }

    private static bool ConsumesMultipartMetadata(System.Collections.IEnumerable? metadata)
    {
        if (metadata == null)
            return false;
        foreach (var item in metadata)
        {
            if (item is ConsumesAttribute consumes
                && consumes.ContentTypes.Any(c =>
                    c.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (item?.GetType().Name.Contains("DisableFormValueModelBinding", StringComparison.Ordinal) == true)
                return true;
        }
        return false;
    }

    private static bool IsFormFileType(Type? type)
    {
        if (type == null)
            return false;
        if (typeof(IFormFile).IsAssignableFrom(type)
            || typeof(IFormCollection).IsAssignableFrom(type)
            || typeof(IFormFileCollection).IsAssignableFrom(type))
            return true;
        var name = type.Name;
        return name.Contains("IFormFile", StringComparison.Ordinal)
            || name.Contains("IFormCollection", StringComparison.Ordinal);
    }

    private static string[] PathSegments(string apiPath) =>
        apiPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static TemplateSegment[] ParseTemplate(string normalizedTemplate) =>
        PathSegments(normalizedTemplate).Select(ParseSegment).ToArray();

    private static TemplateSegment ParseSegment(string segment)
    {
        if (!segment.Contains('{'))
            return new TemplateSegment(false, segment, null, false);
        var inner = segment.Trim('{', '}').TrimEnd('?');
        var catchAll = inner.StartsWith('*');
        inner = inner.TrimStart('*');
        string? constraint = null;
        var colon = inner.IndexOf(':');
        if (colon >= 0)
        {
            constraint = inner[(colon + 1)..];
            var paren = constraint.IndexOf('(');
            if (paren >= 0)
                constraint = constraint[..paren];
        }
        return new TemplateSegment(true, "", constraint, catchAll);
    }

    /// <summary>
    /// Returns the precedence key for a structurally matching template, or null.
    /// Higher keys are more specific: '3' literal, '2' constrained parameter,
    /// '1' parameter, '0' catch-all — compared left to right like endpoint
    /// routing, so a literal sibling beats a parameter route regardless of the
    /// parameter being able to bind the same text.
    /// </summary>
    private static string? MatchKey(TemplateSegment[] template, string[] path)
    {
        var key = new System.Text.StringBuilder(template.Length);
        var pi = 0;
        foreach (var seg in template)
        {
            if (seg.CatchAll)
            {
                key.Append('0');
                return key.ToString();
            }
            if (pi >= path.Length)
                return null;
            if (!seg.IsParameter)
            {
                if (!seg.Literal.Equals(path[pi], StringComparison.OrdinalIgnoreCase))
                    return null;
                key.Append('3');
            }
            else
            {
                var value = path[pi];
                if (value.Length == 0 || !ConstraintMatches(seg.Constraint, value))
                    return null;
                key.Append(seg.Constraint == null ? '1' : '2');
            }
            pi++;
        }
        return pi == path.Length ? key.ToString() : null;
    }

    /// <summary>
    /// Route constraints verified against the path value. An unrecognized
    /// constraint is treated as unbounded — a recorded limit, since only :int
    /// is used in cabinet routes today.
    /// </summary>
    private static bool ConstraintMatches(string? constraint, string value) =>
        constraint switch
        {
            null or "" => true,
            "int" => int.TryParse(value, out _),
            "long" => long.TryParse(value, out _),
            "bool" => bool.TryParse(value, out _),
            "guid" => Guid.TryParse(value, out _),
            "datetime" => DateTime.TryParse(value, out _),
            "decimal" => decimal.TryParse(value, out _),
            "double" or "float" => double.TryParse(value, out _),
            "alpha" => value.All(char.IsLetter),
            "nonalpha" => !value.All(char.IsLetter),
            _ => true,
        };

    /// <summary>Consecutive literal segments matching from the left; a parameter stops the count.</summary>
    private static int LiteralPrefix(string[] templateSegments, string[] pathSegments)
    {
        var count = 0;
        var length = Math.Min(templateSegments.Length, pathSegments.Length);
        for (var i = 0; i < length; i++)
        {
            var segment = templateSegments[i];
            if (segment.Contains('{') || segment.Contains('}'))
                break;
            if (!segment.Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase))
                break;
            count++;
        }
        return count;
    }

    /// <summary>True when the last literal template segment equals the last path segment.</summary>
    private static bool SameTerminalLiteral(string[] templateSegments, string[] pathSegments)
    {
        if (pathSegments.Length == 0 || templateSegments.Length == 0)
            return false;
        var lastLiteral = templateSegments[^1];
        if (lastLiteral.Contains('{') || lastLiteral.Contains('}'))
            return false;
        return lastLiteral.Equals(pathSegments[^1], StringComparison.OrdinalIgnoreCase);
    }

    private static string SplitName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        return Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();
    }
}
