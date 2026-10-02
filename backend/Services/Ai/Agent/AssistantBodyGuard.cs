using System.Text.Json;
using System.Text.RegularExpressions;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Stops an assistant write that would return HTTP 200 and change nothing.
/// Unknown JSON fields are dropped by the serializer. Modifier values that
/// belong in <c>params</c> are rejected here, before the controller runs.
/// </summary>
public static class AssistantBodyGuard
{
    public static string? RejectReason(string method, string path, JsonElement? body)
    {
        if (body is not { ValueKind: JsonValueKind.Object } el)
            return null;
        var action = AssistantBodySchema.Match(method, path);
        if (action == null)
            return null;

        var problems = new List<string>();
        CollectUnknown(action.Body, el, "", problems);
        CollectMisplaced(method, path, el, problems);
        var effect = ModifierEffect(path, el);
        if (effect != null)
            problems.Add(effect);
        if (problems.Count == 0)
            return null;

        var fields = AssistantBodySchema.AcceptedFields(method, path);
        var where = string.IsNullOrEmpty(fields) ? "" : " Accepted body fields: " + fields + ".";
        return string.Join(" ", problems) + where + " The request was not sent.";
    }

    private static void CollectUnknown(AssistantBodySchema.Shape shape, JsonElement el, string prefix, List<string> problems)
    {
        if (shape.Free || shape.Dictionary || el.ValueKind != JsonValueKind.Object)
            return;
        foreach (var prop in el.EnumerateObject())
        {
            if (!shape.Properties.TryGetValue(prop.Name, out var child))
            {
                problems.Add($"{prefix}{prop.Name} is not a field on this request and would be ignored.");
                continue;
            }
            if (child.Dictionary || child.Free)
                continue;
            if (child.Item != null && prop.Value.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        CollectUnknown(child.Item, item, $"{prefix}{prop.Name}[{i}].", problems);
                    i++;
                }
                continue;
            }
            if (prop.Value.ValueKind == JsonValueKind.Object && child.Properties.Count > 0)
                CollectUnknown(child, prop.Value, $"{prefix}{prop.Name}.", problems);
        }
    }

    private static void CollectMisplaced(string method, string path, JsonElement el, List<string> problems)
    {
        var contract = AssistantWriteContract.Match(method, path);
        if (contract == null || contract.BelongsInParams.Length == 0)
            return;
        foreach (var prop in el.EnumerateObject())
        {
            if (contract.BelongsInParams.Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
                problems.Add($"{prop.Name} belongs inside params, not beside modifierType.");
        }
    }

    private static string? ModifierEffect(string path, JsonElement el)
    {
        if (!AssistantWriteContract.PathHas(path, "graph/modifiers"))
            return null;
        var type = ReadString(el, "modifierType");
        if (string.IsNullOrEmpty(type))
            return null;

        var hasParams = el.TryGetProperty("params", out var parms) && parms.ValueKind == JsonValueKind.Object;
        bool Has(string key) => hasParams && Present(parms, key);
        bool Pinned(string key) => HasParamSource(el, key);

        if (type.Equals("regexReplace", StringComparison.OrdinalIgnoreCase)
            || type.Equals("regexExtract", StringComparison.OrdinalIgnoreCase))
        {
            if (!Has("pattern"))
                return "regexReplace and regexExtract require params.pattern inside params. An empty pattern leaves the text unchanged.";
            var pattern = parms.GetProperty("pattern").GetString() ?? "";
            try
            {
                _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50));
            }
            catch (ArgumentException ex)
            {
                return "params.pattern is not a .NET regular expression (" + ex.Message + "). \\u{…} is invalid.";
            }
        }
        else if (type.Equals("replace", StringComparison.OrdinalIgnoreCase) && !Has("search") && !Pinned("search"))
            return "replace requires params.search inside params. An empty search leaves the text unchanged.";
        else if (type.Equals("divide", StringComparison.OrdinalIgnoreCase) && !Has("divisor") && !Pinned("divisor"))
            return "divide requires params.divisor inside params (10 is mm to cm, 1000 is g to kg). A missing divisor divides by 1 and changes nothing.";
        else if (type.Equals("multiply", StringComparison.OrdinalIgnoreCase) && !Has("factor") && !Pinned("factor"))
            return "multiply requires params.factor inside params.";

        return null;
    }

    private static bool HasParamSource(JsonElement el, string param)
    {
        if (!el.TryGetProperty("paramSources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var item in sources.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var name = ReadString(item, "param");
            if (name != null && name.Equals(param, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool Present(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var value))
            return false;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;
        if (value.ValueKind == JsonValueKind.String)
            return !string.IsNullOrWhiteSpace(value.GetString());
        return true;
    }

    private static string? ReadString(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }
}
