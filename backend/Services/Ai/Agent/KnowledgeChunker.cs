using System.Text.RegularExpressions;

namespace Delegat.Services.Ai.Agent;

public sealed record KnowledgeChunk(string SourcePath, string Title, string Body);

public static class KnowledgeChunker
{
    // Each retrieved chunk must fit the model-visible search snippet, including
    // semantic hits that have no literal query term to center an excerpt on.
    private const int MaxBodyChars = 900;

    public static IReadOnlyList<KnowledgeChunk> Chunk(string sourcePath, string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return [];

        var parts = Regex.Split(markdown.Replace("\r\n", "\n"), @"^##\s+", RegexOptions.Multiline);
        var chunks = new List<KnowledgeChunk>();
        for (var i = 0; i < parts.Length; i++)
        {
            var block = parts[i].Trim();
            if (block.Length == 0) continue;
            var nl = block.IndexOf('\n');
            string title;
            string body;
            if (i == 0 && !markdown.TrimStart().StartsWith("## ", StringComparison.Ordinal))
            {
                title = FirstHeading(block) ?? Path.GetFileName(sourcePath);
                body = block;
            }
            else
            {
                title = nl < 0 ? block : block[..nl].Trim();
                body = nl < 0 ? block : block[(nl + 1)..].Trim();
            }

            if (body.Length == 0)
            {
                chunks.Add(new KnowledgeChunk(sourcePath, title, body));
                continue;
            }

            // Large sections must remain searchable; never silently drop their tail.
            for (var start = 0; start < body.Length;)
            {
                var end = Math.Min(start + MaxBodyChars, body.Length);
                if (end < body.Length)
                {
                    var lastBreak = body.LastIndexOfAny(['\n', ' ', '\t'], end - 1, MaxBodyChars);
                    if (lastBreak >= start + MaxBodyChars / 2)
                        end = lastBreak + 1;
                }

                chunks.Add(new KnowledgeChunk(sourcePath, title, body[start..end]));
                start = end;
            }
        }

        return chunks;
    }

    private static string? FirstHeading(string block)
    {
        var m = Regex.Match(block, @"^#\s+(.+)$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}

public static class KnowledgeSearch
{
    /// <summary>Rank the same persisted chunks with lexical and local vector evidence.</summary>
    public static IReadOnlyList<KnowledgeChunk> Hybrid(
        IReadOnlyList<KnowledgeChunk> chunks, string query, IReadOnlyList<float[]?> vectors,
        IReadOnlyList<float> queryVector, int take)
    {
        if (string.IsNullOrWhiteSpace(query) || chunks.Count == 0 || chunks.Count != vectors.Count
            || queryVector.Count == 0)
            return Search(chunks, query, take);

        var terms = Terms(query).ToArray();
        var lexical = Enumerable.Range(0, chunks.Count)
            .Select(i => (index: i, score: Score(chunks[i], terms)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.index)
            .Take(32).Select(x => x.index).ToArray();
        var semantic = Enumerable.Range(0, chunks.Count)
            .Select(i => (index: i, score: Cosine(queryVector, vectors[i])))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.index)
            .Take(32).Select(x => x.index).ToArray();

        var ranks = new Dictionary<int, double>();
        for (var i = 0; i < lexical.Length; i++)
            ranks[lexical[i]] = 1d / (60 + i + 1);
        for (var i = 0; i < semantic.Length; i++)
            ranks[semantic[i]] = ranks.GetValueOrDefault(semantic[i]) + 1d / (60 + i + 1);
        return ranks.OrderByDescending(x => x.Value).ThenBy(x => x.Key)
            .Take(Math.Clamp(take, 1, 32)).Select(x => chunks[x.Key]).ToArray();
    }

    private static double Cosine(IReadOnlyList<float> left, float[]? right)
    {
        if (right == null || left.Count != right.Length) return 0;
        double dot = 0, leftSize = 0, rightSize = 0;
        for (var i = 0; i < right.Length; i++)
        {
            dot += left[i] * right[i];
            leftSize += left[i] * left[i];
            rightSize += right[i] * right[i];
        }
        return leftSize > 0 && rightSize > 0 ? dot / Math.Sqrt(leftSize * rightSize) : 0;
    }

    public static string Snippet(KnowledgeChunk chunk, string query, int maxChars = 900)
    {
        var body = chunk.Body;
        if (body.Length <= maxChars) return body;

        // Prefer the most specific query term found in the body. A relevant hit near
        // the end is useless if the model only receives the beginning of the section.
        var position = Terms(query)
            .Where(term => term.Length >= 3)
            .OrderByDescending(term => term.Length)
            .Select(term => body.IndexOf(term, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(index => index >= 0, 0);
        var start = Math.Clamp(position - maxChars / 3, 0, body.Length - maxChars);
        var end = start + maxChars;
        return (start > 0 ? "…" : "") + body[start..end] + (end < body.Length ? "…" : "");
    }

    public static IReadOnlyList<KnowledgeChunk> Search(
        IReadOnlyList<KnowledgeChunk> chunks,
        string query,
        int take)
    {
        if (string.IsNullOrWhiteSpace(query) || chunks.Count == 0)
            return [];

        var terms = Terms(query).ToArray();
        if (terms.Length == 0)
            return [];

        return chunks
            .Select(c => (chunk: c, score: Score(c, terms)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(Math.Clamp(take, 1, 32))
            .Select(x => x.chunk)
            .ToList();
    }

    private static int Score(KnowledgeChunk chunk, string[] terms)
    {
        var title = chunk.Title.ToLowerInvariant();
        var body = chunk.Body.ToLowerInvariant();
        var path = chunk.SourcePath.ToLowerInvariant();
        var score = 0;
        foreach (var t in terms)
        {
            if (title.Contains(t, StringComparison.Ordinal)) score += 5;
            if (path.Contains(t, StringComparison.Ordinal)) score += 2;
            if (body.Contains(t, StringComparison.Ordinal)) score += 1;
        }

        return score;
    }

    private static IEnumerable<string> Terms(string? query) =>
        Regex.Matches(query ?? "", @"[\p{L}\p{N}]{2,}")
            .Select(match => match.Value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal);
}
