using System.Text.RegularExpressions;

namespace Delegat.Services.Ai.Agent;

public sealed record KnowledgeChunk(string SourcePath, string Title, string Body);

public static class KnowledgeChunker
{
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

            if (body.Length > 4000)
                body = body[..4000];
            chunks.Add(new KnowledgeChunk(sourcePath, title, body));
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
    public static IReadOnlyList<KnowledgeChunk> Search(
        IReadOnlyList<KnowledgeChunk> chunks,
        string query,
        int take)
    {
        if (string.IsNullOrWhiteSpace(query) || chunks.Count == 0)
            return [];

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .Distinct()
            .ToArray();
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
}
