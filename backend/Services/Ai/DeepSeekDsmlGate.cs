using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Delegat.Services.Ai;

/// <summary>
/// DeepSeek sometimes writes a tool call as text
/// (<c>&lt;｜DSML｜calls&gt;</c> / <c>&lt;｜DSML｜invoke&gt;</c>) instead of an OpenAI tool_calls delta.
/// Hold that markup out of the visible reply and turn each invoke into a normal tool call.
/// </summary>
public sealed class DeepSeekDsmlGate
{
    private static readonly string[] TagNames = ["tool_calls", "function_calls", "calls", "invoke"];

    private static readonly Regex OpenTag = new(
        @"<\s*[|｜]+\s*DSML\s*[|｜]+\s*(?<tag>tool_calls|function_calls|calls|invoke)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CallsClose = new(
        @"</\s*[|｜]+\s*DSML\s*[|｜]+\s*(?:tool_|function_)?calls\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex InvokeBlock = new(
        @"<\s*[|｜]+\s*DSML\s*[|｜]+\s*invoke\b(?<attrs>[^>]*)>(?<body>.*?)</\s*[|｜]+\s*DSML\s*[|｜]+\s*invoke\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Parameter = new(
        @"<\s*[|｜]+\s*DSML\s*[|｜]+\s*parameter\b(?<attrs>[^>]*)>(?<body>.*?)</\s*[|｜]+\s*DSML\s*[|｜]+\s*parameter\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Attribute = new(
        @"\b(?<key>[A-Za-z_][\w-]*)\s*=\s*(?:""(?<val>[^""]*)""|'(?<val>[^']*)')",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly StringBuilder _hold = new();

    public readonly record struct Invoke(string Name, string ArgumentsJson);

    public readonly record struct Drain(string Visible, IReadOnlyList<Invoke> Invokes);

    public Drain Push(string? chunk)
    {
        if (!string.IsNullOrEmpty(chunk))
            _hold.Append(chunk);
        return DrainHold(flush: false);
    }

    public Drain Flush() => DrainHold(flush: true);

    private Drain DrainHold(bool flush)
    {
        var text = _hold.ToString();
        var visible = new StringBuilder();
        var invokes = new List<Invoke>();
        var cursor = 0;

        while (cursor < text.Length)
        {
            var open = OpenTag.Match(text, cursor);
            if (!open.Success)
            {
                var tail = UnfinishedOpenLength(text, cursor);
                if (flush && tail > 0 && text.AsSpan(text.Length - tail).Contains("DSML", StringComparison.OrdinalIgnoreCase))
                {
                    invokes.AddRange(ParseInvokes(text[cursor..]));
                    cursor = text.Length;
                    break;
                }
                var emit = text.Length - (flush ? 0 : tail) - cursor;
                if (emit > 0)
                    visible.Append(text, cursor, emit);
                cursor = text.Length - (flush ? 0 : tail);
                break;
            }

            if (open.Index > cursor)
                visible.Append(text, cursor, open.Index - cursor);

            var isCalls = open.Groups["tag"].Value.Contains("call", StringComparison.OrdinalIgnoreCase);
            var close = isCalls ? CallsClose.Match(text, open.Index + open.Length) : null;
            if (isCalls && close is { Success: true })
            {
                var end = close.Index + close.Length;
                invokes.AddRange(ParseInvokes(text[open.Index..end]));
                cursor = end;
                continue;
            }

            if (!isCalls)
            {
                var invoke = InvokeBlock.Match(text, open.Index);
                if (invoke.Success && invoke.Index == open.Index)
                {
                    AddInvoke(invokes, invoke);
                    cursor = invoke.Index + invoke.Length;
                    continue;
                }
            }

            if (!flush)
            {
                cursor = open.Index;
                break;
            }

            invokes.AddRange(ParseInvokes(text[open.Index..]));
            cursor = text.Length;
            break;
        }

        _hold.Clear();
        if (cursor < text.Length)
            _hold.Append(text, cursor, text.Length - cursor);

        return new Drain(visible.ToString(), invokes);
    }

    internal static IReadOnlyList<Invoke> ParseInvokes(string markup)
    {
        var invokes = new List<Invoke>();
        foreach (Match invoke in InvokeBlock.Matches(markup))
            AddInvoke(invokes, invoke);
        return invokes;
    }

    private static void AddInvoke(List<Invoke> invokes, Match invoke)
    {
        var name = Attr(invoke.Groups["attrs"].Value, "name");
        if (string.IsNullOrWhiteSpace(name)) return;
        const string prefix = "functions.";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            name = name[prefix.Length..];
        if (name.Length == 0) return;
        invokes.Add(new Invoke(name, ArgumentsJson(invoke.Groups["body"].Value)));
    }

    private static string ArgumentsJson(string body)
    {
        var obj = new JsonObject();
        foreach (Match param in Parameter.Matches(body))
        {
            var name = Attr(param.Groups["attrs"].Value, "name");
            if (string.IsNullOrEmpty(name)) continue;
            var raw = param.Groups["body"].Value.Trim();
            var asString = !string.Equals(Attr(param.Groups["attrs"].Value, "string"), "false", StringComparison.OrdinalIgnoreCase);
            obj[name] = asString ? JsonValue.Create(raw) : LooseJson(raw);
        }
        return obj.ToJsonString();
    }

    private static JsonNode LooseJson(string raw)
    {
        if (raw.Length == 0) return JsonValue.Create(string.Empty)!;
        try
        {
            return JsonNode.Parse(raw) ?? JsonValue.Create(raw)!;
        }
        catch (JsonException)
        {
            return JsonValue.Create(raw)!;
        }
    }

    private static string? Attr(string attrs, string key)
    {
        foreach (Match match in Attribute.Matches(attrs))
        {
            if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
                return match.Groups["val"].Value;
        }
        return null;
    }

    /// <summary>Length of a trailing <c>&lt;</c> that could still grow into a DSML open tag.</summary>
    private static int UnfinishedOpenLength(string text, int cursor)
    {
        var lt = text.LastIndexOf('<', text.Length - 1, text.Length - cursor);
        if (lt < cursor) return 0;
        var tail = text[lt..];
        if (OpenTag.IsMatch(tail)) return 0;
        return CouldBeOpenTag(tail) ? tail.Length : 0;
    }

    private static bool CouldBeOpenTag(string s)
    {
        if (s.Length == 0 || s[0] != '<') return false;
        var i = 1;
        while (i < s.Length && s[i] is ' ' or '\t') i++;
        if (i >= s.Length) return true;
        if (s[i] is not ('｜' or '|')) return false;
        while (i < s.Length && s[i] is '｜' or '|') i++;
        if (i >= s.Length) return true;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i >= s.Length) return true;

        const string word = "DSML";
        var w = 0;
        while (i < s.Length && w < word.Length && char.ToUpperInvariant(s[i]) == word[w])
        {
            i++;
            w++;
        }
        if (w < word.Length) return i >= s.Length;
        if (i >= s.Length) return true;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i >= s.Length) return true;
        if (s[i] is not ('｜' or '|')) return false;
        while (i < s.Length && s[i] is '｜' or '|') i++;
        if (i >= s.Length) return true;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i >= s.Length) return true;

        var nameStart = i;
        while (i < s.Length && (char.IsLetter(s[i]) || s[i] == '_')) i++;
        var name = s[nameStart..i];
        if (name.Length == 0) return true;
        var known = false;
        var prefix = false;
        foreach (var tag in TagNames)
        {
            if (tag.Equals(name, StringComparison.OrdinalIgnoreCase)) known = true;
            if (tag.StartsWith(name, StringComparison.OrdinalIgnoreCase)) prefix = true;
        }
        if (!prefix) return false;
        if (i >= s.Length) return true;
        return known;
    }
}
