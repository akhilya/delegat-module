using System.Text.RegularExpressions;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Fails when a prompt, the platform_request surface, or search_actions
/// describes a body the controller does not read, or omits the shared contract
/// sentence for a path it already mentions.
/// </summary>
public static class AssistantInstructionAudit
{
    public static IReadOnlyList<string> Check(IEnumerable<(string Source, string Text, bool RequireSentence)> sources)
    {
        var errors = new List<string>();
        var list = sources.ToList();
        foreach (var entry in AssistantWriteContract.All)
        {
            var shape = FindAction(entry);
            if (shape == null)
            {
                errors.Add($"No FromBody action for {entry.Method} {entry.PathSuffix}.");
                continue;
            }

            foreach (var key in entry.RootKeys)
            {
                if (!shape.Body.Properties.ContainsKey(key))
                    errors.Add($"{entry.PathSuffix} contract lists {key}, but {shape.BodyType.Name} does not.");
            }

            foreach (var (source, text, require) in list)
            {
                if (!require || !Mentions(text, entry.PathSuffix))
                    continue;
                var needle = entry.Method + " " + entry.PathSuffix + " " + entry.Sentence;
                if (!text.Contains(needle, StringComparison.Ordinal))
                    errors.Add($"{source} mentions {entry.PathSuffix} but not the body contract.");
            }
        }

        foreach (var (source, text, _) in list)
        {
            foreach (var sketch in Sketches(text))
            {
                var action = MatchAction(sketch);
                if (action == null)
                    continue;
                var contract = AssistantWriteContract.Match(action.Method, action.Template);
                CheckKeys(source, action, contract, sketch.Keys, sketch.Nested, "", errors);
            }
        }

        return errors;
    }

    private static void CheckKeys(
        string source,
        AssistantBodySchema.Action action,
        AssistantWriteContract.Entry? contract,
        IReadOnlyList<string> keys,
        IReadOnlyDictionary<string, Sketch> nested,
        string prefix,
        List<string> errors)
    {
        var shape = prefix.Length == 0 ? action.Body : null;
        foreach (var key in keys)
        {
            if (prefix.Length == 0 && contract != null
                && contract.BelongsInParams.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{source} puts {key} beside the body of {action.Suffix}. It belongs inside params.");
                continue;
            }

            if (prefix.Length == 0)
            {
                if (!action.Body.Properties.TryGetValue(key, out shape))
                {
                    errors.Add($"{source} tells the assistant to send {key} on {action.Suffix}, but {action.BodyType.Name} does not read it.");
                    continue;
                }
            }

            if (!nested.TryGetValue(key, out var child))
                continue;
            if (shape == null)
                continue;
            if (shape.Dictionary || shape.Free)
                continue;
            var item = shape.Item ?? (shape.Properties.Count > 0 ? shape : null);
            if (item == null)
                continue;
            foreach (var nestedKey in child.Keys)
            {
                if (item.Dictionary || item.Free)
                    continue;
                if (!item.Properties.ContainsKey(nestedKey))
                    errors.Add($"{source} tells the assistant to send {key}.{nestedKey} on {action.Suffix}, but that object does not read it.");
            }
        }
    }

    private static AssistantBodySchema.Action? FindAction(AssistantWriteContract.Entry entry)
    {
        var hit = AssistantBodySchema.Actions.FirstOrDefault(a =>
            a.Method.Equals(entry.Method, StringComparison.OrdinalIgnoreCase)
            && (a.Suffix.Equals(entry.PathSuffix, StringComparison.OrdinalIgnoreCase)
                || a.Template.EndsWith("/" + entry.PathSuffix, StringComparison.OrdinalIgnoreCase)));
        if (hit != null)
            return hit;
        if (entry.PathSuffix.Equals("export-all-prices/run", StringComparison.OrdinalIgnoreCase))
        {
            return AssistantBodySchema.Actions.FirstOrDefault(a =>
                a.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && a.Template.Contains("/tools/", StringComparison.OrdinalIgnoreCase)
                && a.Suffix.Equals("run", StringComparison.OrdinalIgnoreCase)
                && a.Body.Properties.ContainsKey("connectionId"));
        }
        return null;
    }

    private static AssistantBodySchema.Action? MatchAction(Sketch sketch)
    {
        var path = LastPath(sketch.Before);
        if (path.Length == 0)
            return null;
        var method = sketch.Method ?? "POST";
        return AssistantBodySchema.Match(method, path);
    }

    private static readonly Regex PathSegment = new(
        @"/(?:\{[A-Za-z0-9_]+\}|[A-Za-z0-9_.…-]+)",
        RegexOptions.Compiled);

    private static string LastPath(string before)
    {
        var matches = PathSegment.Matches(before);
        if (matches.Count == 0)
            return "";
        var start = matches.Count - 1;
        while (start > 0)
        {
            var prev = matches[start - 1];
            var gap = before[(prev.Index + prev.Length)..matches[start].Index];
            if (gap.Any(char.IsAsciiLetter))
                break;
            start--;
        }
        var pathEnd = matches[^1].Index + matches[^1].Length;
        var tail = before[pathEnd..].Trim().Trim('`', '"', '\'');
        if (tail.Length > 0
            && !tail.Equals("Body", StringComparison.OrdinalIgnoreCase)
            && tail.Any(char.IsAsciiLetter))
            return "";
        return string.Concat(matches.Skip(start).Select(m => m.Value));
    }

    private static bool Mentions(string text, string suffix) =>
        text.Contains(suffix, StringComparison.OrdinalIgnoreCase);

    private sealed class Sketch
    {
        public string Before { get; set; } = "";
        public string? Method { get; set; }
        public List<string> Keys { get; init; } = [];
        public Dictionary<string, Sketch> Nested { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<Sketch> Sketches(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
                continue;
            if (i > 0 && (text[i - 1] == '/' || text[i - 1] == '\\'))
                continue;
            var look = i - 1;
            while (look >= 0 && char.IsWhiteSpace(text[look]))
                look--;
            if (look >= 0 && text[look] is '[' or ':')
                continue;
            var end = Close(text, i);
            if (end < 0)
                continue;
            var sketch = ParseObject(text[(i + 1)..end]);
            if (sketch.Keys.Count == 0)
                continue;
            var from = Math.Max(0, i - 280);
            sketch.Before = text[from..i];
            sketch.Method = Verb(sketch.Before);
            yield return sketch;
            i = end;
        }
    }

    private static Sketch ParseObject(string inner)
    {
        var sketch = new Sketch();
        foreach (var part in Split(inner))
        {
            var piece = part.Trim();
            if (piece.Length == 0)
                continue;
            var colon = TopColon(piece);
            var rawKey = colon >= 0 ? piece[..colon] : piece;
            var rest = colon >= 0 ? piece[(colon + 1)..].Trim() : null;
            foreach (var key in rawKey.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var name = key.Trim().Trim('"').TrimEnd('?').Trim().Trim('"');
                if (name.Contains('.'))
                {
                    var bits = name.Split('.', 2);
                    if (!Ident(bits[0]) || !Ident(bits[1]))
                        continue;
                    sketch.Keys.Add(bits[0]);
                    sketch.Nested[bits[0]] = new Sketch { Keys = [bits[1]] };
                    continue;
                }
                if (!Ident(name))
                    continue;
                sketch.Keys.Add(name);
                if (rest == null || sketch.Nested.ContainsKey(name))
                    continue;
                var nested = NestedSketch(rest);
                if (nested != null)
                    sketch.Nested[name] = nested;
            }
        }
        return sketch;
    }

    private static Sketch? NestedSketch(string rest)
    {
        rest = rest.Trim();
        if (rest.StartsWith('['))
            rest = rest.Trim()[1..].TrimStart();
        var brace = rest.IndexOf('{');
        if (brace < 0)
            return null;
        var end = Close(rest, brace);
        if (end < 0)
            return null;
        var parsed = ParseObject(rest[(brace + 1)..end]);
        return parsed.Keys.Count == 0 ? null : parsed;
    }

    private static int Close(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    private static List<string> Split(string inner)
    {
        var parts = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = false;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c == '"' && (i == 0 || inner[i - 1] != '\\'))
                quote = !quote;
            if (quote)
                continue;
            if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(inner[start..i]);
                start = i + 1;
            }
        }
        parts.Add(inner[start..]);
        return parts;
    }

    private static int TopColon(string piece)
    {
        var depth = 0;
        var quote = false;
        for (var i = 0; i < piece.Length; i++)
        {
            var c = piece[i];
            if (c == '"' && (i == 0 || piece[i - 1] != '\\'))
                quote = !quote;
            if (quote)
                continue;
            if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
            else if (c == ':' && depth == 0)
                return i;
        }
        return -1;
    }

    private static string? Verb(string before)
    {
        string? found = null;
        var at = -1;
        foreach (var verb in new[] { "PATCH", "POST", "PUT", "DELETE" })
        {
            var i = before.LastIndexOf(verb, StringComparison.OrdinalIgnoreCase);
            if (i > at)
            {
                at = i;
                found = verb;
            }
        }
        return found;
    }

    private static bool Ident(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
