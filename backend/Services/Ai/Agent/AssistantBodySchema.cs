using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// JSON shapes of cabinet <c>[FromBody]</c> arguments, read from the controllers.
/// This is what a write actually accepts. Instructions are checked against it.
/// </summary>
public static class AssistantBodySchema
{
    public sealed class Shape
    {
        public bool Dictionary { get; init; }
        public bool Free { get; init; }
        public Dictionary<string, Shape> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Shape? Item { get; init; }
        /// <summary>Reflected enum members: name → numeric underlying value.</summary>
        public IReadOnlyDictionary<string, long>? EnumValues { get; init; }
    }

    public sealed record Action(string Method, string Template, string Suffix, Type BodyType, Shape Body);

    private static readonly Lazy<IReadOnlyList<Action>> Cached = new(Discover, isThreadSafe: true);

    public static IReadOnlyList<Action> Actions => Cached.Value;

    public static Action? Match(string method, string path)
    {
        return MatchCore(method, path, suffix: false) ?? MatchCore(method, path, suffix: true);
    }

    private static Action? MatchCore(string method, string path, bool suffix)
    {
        var norm = Normalize(path);
        Action? best = null;
        var bestScore = -1;
        foreach (var action in Cached.Value)
        {
            if (!action.Method.Equals(method, StringComparison.OrdinalIgnoreCase))
                continue;
            var ok = suffix
                ? RouteSuffixMatches(action.Template, norm, out var score)
                : RouteMatches(action.Template, norm, out score);
            if (!ok || score < bestScore)
                continue;
            if (score == bestScore && best != null && action.Template.Length <= best.Template.Length)
                continue;
            best = action;
            bestScore = score;
        }
        return best;
    }

    public static string? AcceptedFields(string method, string path)
    {
        var action = Match(method, path);
        if (action == null || action.Body.Properties.Count == 0)
            return null;
        var hidden = AssistantWriteContract.Match(method, path)?.BelongsInParams ?? [];
        var names = action.Body.Properties.Keys
            .Where(k => !hidden.Contains(k, StringComparer.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
        var list = string.Join(", ", names);
        return list.Length == 0 ? null : list;
    }

    /// <summary>
    /// Deterministic enum member guidance for root body properties, e.g.
    /// "Allowed type values: Text=0, Number=1, …, File=6." One clause per enum
    /// property, in declaration order; null when the action has none.
    /// </summary>
    internal static string? EnumFieldGuidance(string method, string path)
    {
        var action = Match(method, path);
        if (action == null)
            return null;
        var clauses = action.Body.Properties
            .Where(p => p.Value.EnumValues is { Count: > 0 })
            .Select(p => $"Allowed {p.Key} values: "
                + string.Join(", ", p.Value.EnumValues!.Select(v =>
                    $"{v.Key}={v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))
                + ".")
            .ToList();
        return clauses.Count == 0 ? null : string.Join(' ', clauses);
    }

    /// <summary>Bounded DTO field names, excluding selectors and nested item identifiers.</summary>
    internal static IReadOnlyList<string> UpdateFields(string method, string path)
    {
        var action = Match(method, path);
        if (action == null) return [];
        var shape = action.Body.Properties.GetValueOrDefault("updates") ?? action.Body;
        return shape.Properties
            .Where(p => (p.Value.Item == null && p.Value.Properties.Count == 0 && !p.Value.Dictionary && !p.Value.Free)
                || p.Key is "attributeValues" or "storeAttributeValues")
            .Select(p => p.Key)
            .Where(k => k is not ("productIds" or "allMatching" or "where" or "wait" or "id"))
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    internal static string Normalize(string path)
    {
        var q = path.IndexOf('?');
        var p = q >= 0 ? path[..q] : path;
        if (p.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            p = p["/api".Length..];
        if (!p.StartsWith('/'))
            p = "/" + p;
        return p.TrimEnd('/');
    }

    private static bool RouteMatches(string template, string path, out int literalScore)
    {
        literalScore = 0;
        var t = Normalize(template).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var p = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length != p.Length)
            return false;
        for (var i = 0; i < t.Length; i++)
        {
            if (!SegmentMatches(t[i], p[i], ref literalScore))
                return false;
        }
        return literalScore > 0;
    }

    private static bool RouteSuffixMatches(string template, string path, out int literalScore)
    {
        literalScore = 0;
        var t = Normalize(template).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var p = Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || p.Length > t.Length)
            return false;
        var offset = t.Length - p.Length;
        for (var i = 0; i < p.Length; i++)
        {
            var seg = p[i];
            if (seg is "…" or "...")
                continue;
            if (!SegmentMatches(t[offset + i], seg, ref literalScore))
                return false;
        }
        return literalScore > 0;
    }

    private static bool SegmentMatches(string template, string path, ref int literalScore)
    {
        var pathParam = path.StartsWith('{') && path.EndsWith('}');
        var templateParam = template.StartsWith('{') && template.EndsWith('}');
        if (pathParam)
            return templateParam;
        if (templateParam)
            return true;
        if (!template.Equals(path, StringComparison.OrdinalIgnoreCase))
            return false;
        literalScore++;
        return true;
    }

    private static List<Action> Discover()
    {
        var list = new List<Action>();
        var asm = typeof(AssistantBodySchema).Assembly;
        foreach (var type in asm.GetTypes())
        {
            if (type.IsAbstract || !typeof(ControllerBase).IsAssignableFrom(type))
                continue;
            var ns = type.Namespace ?? "";
            if (ns.Contains("PublicApi", StringComparison.Ordinal))
                continue;

            var prefix = type.GetCustomAttributes<RouteAttribute>(inherit: true)
                .Select(r => r.Template)
                .FirstOrDefault(t => !string.IsNullOrEmpty(t));

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.DeclaringType == typeof(ControllerBase) || method.IsSpecialName)
                    continue;
                var verbs = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .SelectMany(a => a.HttpMethods)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (verbs.Count == 0)
                    continue;

                var templates = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .Select(a => a.Template)
                    .Concat(method.GetCustomAttributes<RouteAttribute>(inherit: true).Select(r => r.Template))
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (templates.Count == 0)
                    continue;

                var body = method.GetParameters()
                    .FirstOrDefault(p => p.GetCustomAttribute<FromBodyAttribute>() != null);
                if (body == null)
                    continue;
                var bodyType = Nullable.GetUnderlyingType(body.ParameterType) ?? body.ParameterType;
                if (bodyType == typeof(JsonElement) || bodyType == typeof(JsonDocument) || bodyType == typeof(string))
                    continue;

                var shape = Build(bodyType, 0, []);
                foreach (var verb in verbs)
                {
                    foreach (var template in templates)
                    {
                        var full = Combine(prefix, template!);
                        list.Add(new Action(
                            verb.ToUpperInvariant(),
                            full,
                            TrailingLiterals(full),
                            bodyType,
                            shape));
                    }
                }
            }
        }
        return list;
    }

    private static string Combine(string? prefix, string template)
    {
        if (template.StartsWith('/'))
            return template;
        if (string.IsNullOrEmpty(prefix))
            return template.StartsWith('/') ? template : "/" + template;
        var left = prefix.TrimEnd('/');
        if (!left.StartsWith('/'))
            left = "/" + left;
        return left + "/" + template.TrimStart('/');
    }

    internal static string TrailingLiterals(string template)
    {
        var parts = Normalize(template).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tail = new List<string>();
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i].StartsWith('{'))
                break;
            tail.Add(parts[i]);
        }
        tail.Reverse();
        return string.Join('/', tail);
    }

    private static Shape Build(Type type, int depth, HashSet<Type> stack)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(JsonElement) || type == typeof(JsonDocument))
            return new Shape { Free = true };
        if (IsDictionary(type))
            return new Shape { Dictionary = true };
        var itemType = ElementType(type);
        if (itemType != null)
            return new Shape { Item = Build(itemType, depth + 1, stack) };
        if (type.IsEnum)
            return new Shape { EnumValues = EnumShape(type) };
        if (depth > 4 || IsScalar(type) || !stack.Add(type))
            return new Shape();

        var shape = new Shape();
        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                continue;
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                continue;
            if (prop.GetCustomAttribute<JsonExtensionDataAttribute>() != null)
                continue;
            var name = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            shape.Properties[name] = Build(prop.PropertyType, depth + 1, stack);
        }
        stack.Remove(type);
        return shape;
    }

    private static IReadOnlyDictionary<string, long> EnumShape(Type type)
    {
        var names = Enum.GetNames(type);
        var values = Enum.GetValuesAsUnderlyingType(type);
        var map = new Dictionary<string, long>(names.Length, StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
            map[names[i]] = Convert.ToInt64(
                values.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
        return map;
    }

    private static bool IsDictionary(Type type)
    {
        if (!type.IsGenericType)
            return typeof(IDictionary).IsAssignableFrom(type);
        var def = type.GetGenericTypeDefinition();
        return def == typeof(Dictionary<,>)
            || def == typeof(IDictionary<,>)
            || def == typeof(IReadOnlyDictionary<,>);
    }

    private static Type? ElementType(Type type)
    {
        if (type == typeof(string) || IsScalar(type))
            return null;
        if (type.IsArray)
            return type.GetElementType();
        if (!type.IsGenericType)
            return null;
        var def = type.GetGenericTypeDefinition();
        if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>)
            || def == typeof(ICollection<>) || def == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];
        return null;
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(Guid)
            || type == typeof(TimeSpan);
    }
}
