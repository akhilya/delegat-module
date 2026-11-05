using System.Diagnostics;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace Delegat.Tests;

/// <summary>
/// Provider-free retrieval fixtures. These are not a measurement of a live model or
/// the production document corpus; use the same cases when evaluating a hybrid index.
/// </summary>
public class KnowledgeRetrievalEvaluationTests(ITestOutputHelper output)
{
    private static readonly KnowledgeChunk[] Corpus =
    [
        new("docs/guides/shop-export.md", "Publish products",
            "Send existing catalog cards to a connected storefront after user approval."),
        new("docs/guides/mapping.md", "Field mappings",
            "Associate each shop field with a local attribute or a static value."),
        new("docs/guides/inventory-ru.md", "Остатки",
            "Проверьте количество товаров на складе перед отправкой изменений."),
        new("docs/guides/returns.md", "Returns",
            "Review reverse shipments and accepted orders."),
        new("docs/guides/pricing.md", "Price updates",
            "Change the catalog price and queue a marketplace price sync."),
        new("docs/guides/import.md", "Import products",
            "Bring remote offers into the local catalog and resolve duplicates."),
        new("docs/guides/media.md", "Images and files",
            "Attach photos to a product or download its existing images."),
        new("docs/guides/backup.md", "Backups",
            "Restore a stored snapshot after a database failure."),
        new("docs/guides/orders.md", "Orders",
            "Open new orders and prepare shipments for delivery."),
        new("docs/guides/permissions.md", "Account permissions",
            "Grant access to operators and control who can edit the catalog."),
    ];

    private static readonly (string Query, string Source, bool Exact)[] Cases =
    [
        ("publish products", "docs/guides/shop-export.md", true),
        ("field mappings", "docs/guides/mapping.md", true),
        ("остатки на складе", "docs/guides/inventory-ru.md", true),
        ("refresh seller listings", "docs/guides/shop-export.md", false),
        ("как перевыгрузить карточки на площадку", "docs/guides/shop-export.md", false),
        ("how are seller attributes wired to source columns", "docs/guides/mapping.md", false),
    ];

    [Fact]
    public void LexicalBaseline_RecordsExactAndSemanticRecallAtThree()
    {
        var exactTopOne = 0;
        var exactTopThree = 0;
        var semanticTopOne = 0;
        var semanticTopThree = 0;
        foreach (var (query, source, exact) in Cases)
        {
            var hits = KnowledgeSearch.Search(Corpus, query, take: 3);
            var topOne = hits.FirstOrDefault()?.SourcePath == source;
            var topThree = hits.Any(hit => hit.SourcePath == source);
            if (exact)
            {
                if (topOne) exactTopOne++;
                if (topThree) exactTopThree++;
            }
            else
            {
                if (topOne) semanticTopOne++;
                if (topThree) semanticTopThree++;
            }
            output.WriteLine($"{(exact ? "exact" : "paraphrase/cross-language")}: "
                + $"{query} => top1={topOne}, top3={topThree} ({source})");
        }

        output.WriteLine($"Lexical source recall@1/@3: exact {exactTopOne}/{exactTopThree} of 3, "
            + $"paraphrase/cross-language {semanticTopOne}/{semanticTopThree} of 3");
        Assert.Equal(3, exactTopOne);
    }

    [Fact]
    public void HybridFusion_UsesSemanticCandidatesWithoutLosingExactMatches()
    {
        // Synthetic vectors test ranking and fallback, not the quality of any embedding model.
        float[]?[] vectors = [[1, 0], [0, 1], [0, 0.8f], [-1, 0], null, null, null, null, null, null];
        var crossLanguage = KnowledgeSearch.Hybrid(Corpus,
            "как перевыгрузить карточки на площадку", vectors, [1, 0], take: 1);
        var exact = KnowledgeSearch.Hybrid(Corpus, "field mappings", vectors, [0, 1], take: 1);

        Assert.Equal("docs/guides/shop-export.md", Assert.Single(crossLanguage).SourcePath);
        Assert.Equal("docs/guides/mapping.md", Assert.Single(exact).SourcePath);
    }

    [Fact]
    public async Task LocalModelPilot_ReportsPairedRecallWhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("AI_KNOWLEDGE_VECTOR_ENABLED"),
                "true", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("Local-model recall: not measured (pilot disabled).");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var settings = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var model = new KnowledgeEmbeddingClient(http, settings);
        Assert.True(model.IsEnabled, "Set a local AI_KNOWLEDGE_OLLAMA_URL for the pilot");
        var timer = Stopwatch.StartNew();
        var vectors = (await model.EmbedAsync(
            Corpus.Select(c => KnowledgeEmbeddingInputs.Document(
                c.Title, c.Body, model.ModelId)).ToArray(), default))
            .Select(vector => (float[]?)vector).ToArray();
        output.WriteLine($"Local embedding of {Corpus.Length} documents: {timer.ElapsedMilliseconds} ms");
        timer.Restart();

        var exactTopOne = 0;
        var exactTopThree = 0;
        var paraphraseTopOne = 0;
        var paraphraseTopThree = 0;
        var queryDurations = new List<long>();
        foreach (var (query, source, isExact) in Cases)
        {
            var queryTimer = Stopwatch.StartNew();
            var queryVector = Assert.Single(await model.EmbedAsync(
                [KnowledgeEmbeddingInputs.Query(query, model.ModelId)], default));
            var hits = KnowledgeSearch.Hybrid(Corpus, query, vectors, queryVector, take: 3);
            queryDurations.Add(queryTimer.ElapsedMilliseconds);
            var topOne = hits.FirstOrDefault()?.SourcePath == source;
            var topThree = hits.Any(hit => hit.SourcePath == source);
            if (isExact)
            {
                if (topOne) exactTopOne++;
                if (topThree) exactTopThree++;
            }
            else
            {
                if (topOne) paraphraseTopOne++;
                if (topThree) paraphraseTopThree++;
            }
            output.WriteLine($"{query} => top1={topOne}, top3={topThree}, "
                + $"hits=[{string.Join(", ", hits.Select(hit => hit.SourcePath))}], "
                + $"latency={queryDurations[^1]} ms");
        }
        output.WriteLine($"Local model hybrid source recall@1/@3: exact "
            + $"{exactTopOne}/{exactTopThree} of 3, paraphrase/cross-language "
            + $"{paraphraseTopOne}/{paraphraseTopThree} of 3");
        output.WriteLine($"Embedding and retrieval for {Cases.Length} queries: {timer.ElapsedMilliseconds} ms");
        queryDurations.Sort();
        output.WriteLine($"Warm query p50/p95 (six synthetic cases, not production): "
            + $"{queryDurations[(queryDurations.Count - 1) / 2]}/"
            + $"{queryDurations[(int)Math.Ceiling(queryDurations.Count * 0.95) - 1]} ms");
        Assert.Equal(3, exactTopThree);
    }
}
