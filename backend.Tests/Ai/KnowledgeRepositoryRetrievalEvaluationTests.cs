using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Configuration;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Docs;
using Delegat.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Delegat.Tests;

/// <summary>
/// Offline retrieval evaluation against the repository's current, locally indexed documentation.
/// Gold evidence is reviewed independently of the search results. Public docs are fetched only by opt-in.
/// </summary>
public class KnowledgeRepositoryRetrievalEvaluationTests(ITestOutputHelper output)
{
    // Match the actual tool-result serializer in AiChatService, including non-ASCII text.
    private static readonly JsonSerializerOptions ModelToolJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record AlternativeEvidence(string Source, string Text);
    private sealed record Case(string Query, string Source, string Evidence,
        params AlternativeEvidence[] Alternatives);

    private static readonly Case[] Cases =
    [
        new("How do I export a named set of products to a storefront?",
            "docs/ai/operator-playbook.md", "productIds"),
        new("Как поднять стоимость всех карточек на одинаковую сумму и отправить новые цены?",
            "docs/ai/operator-playbook.md", "priceDelta"),
        new("Can Avito publish a product card directly from the cabinet?",
            "docs/adapters/avito.md", "full product-card publishing is not available"),
        new("Почему в OpenCart для обмена данными нужен отдельный модуль на стороне магазина?",
            "docs/adapters/opencart.md", "bridge endpoint"),
        new("How do WooCommerce photos get uploaded without pulling public image URLs?",
            "docs/adapters/woocommerce.md", "IMediaBlobReader"),
        new("Что происходит с фото для WooCommerce, если архивный endpoint отвечает 404?",
            "docs/adapters/woocommerce.md", "legacy URL-queue"),
        new("How long is a private product file download link valid?",
            "docs/api/mcp.md", "10-minute ticket"),
        new("Можно ли скачать файл товара через MCP, не передавая байты модели?",
            "docs/api/mcp.md", "No file bytes pass through MCP JSON"),
        new("Can I sign in with a passkey without entering an authenticator code?",
            "docs/architecture/authentication.md", "does not require TOTP"),
        new("Where does the version shown in the user menu come from?",
            "docs/platform/releasing.md", "InformationalVersion"),
        new("What should I check before exporting listings from a report when some cards are unlinked?",
            "docs/architecture/ai-assistant.md", "product_links"),
        new("Может ли сотрудник с правами только на просмотр запускать Python в агенте?",
            "docs/api/mcp.md", "No Python"),
    ];

    private static readonly string[] OutOfScopeQueries =
    [
        "What will the weather be in Paris tomorrow?",
        "Write a poem about the moon and stars.",
        "How do I bake sourdough bread at home?",
        "What is the population of Tokyo?",
    ];

    private static readonly Case[] PublicCases =
    [
        new("Почему после удаления карточек импорт всё ещё упирается в число товаров тарифа?",
            "product-docs/ru/guides/restore-deleted.md", "Слот тарифа считает и корзину"),
        new("Why does deleting catalog cards not let me import more products on my plan?",
            "product-docs/en/guides/restore-deleted.md", "plan slot counts the trash too",
            new AlternativeEvidence("product-docs/en/help/troubleshooting.md",
                "The product limit includes the trash. Import stops when slots are gone.")),
        new("Удалит ли повторный импорт с другой площадки существующие локальные товары?",
            "product-docs/ru/workspace/catalog-connect.md", "не удаляются и не заменяются"),
        new("Will importing from another sales channel overwrite the products already in my catalog?",
            "product-docs/en/workspace/catalog-connect.md", "not deleted or replaced"),
        new("Сможет ли площадка забрать фотографии по ссылке, доступной только внутри моей сети?",
            "product-docs/ru/workspace/files.md", "должны открываться из интернета"),
        new("Can a marketplace fetch product photos from links reachable only inside my network?",
            "product-docs/en/workspace/files.md", "must open from the internet"),
    ];

    // Authored before the title-context experiment; report separately from the initial 18 cases.
    private static readonly Case[] ValidationCases =
    [
        new("In what order does Connect catalogs try identifiers when matching existing cards?",
            "product-docs/en/workspace/catalog-connect.md", "Matches are found by SKU, then barcode"),
        new("В каком порядке Связать каталоги проверяет артикул и штрихкод при поиске совпадений?",
            "product-docs/ru/workspace/catalog-connect.md", "по артикулу, затем по штрихкоду"),
        new("When I restore a trashed product, do its category and marketplace links survive?",
            "product-docs/en/guides/restore-deleted.md", "Category and channel links are kept"),
        new("Сохранятся ли категория и привязки к площадкам при восстановлении карточки из корзины?",
            "product-docs/ru/guides/restore-deleted.md", "Категория и связи с площадками сохраняются"),
        new("When moving a product between stores, whose storage allowance is checked?",
            "product-docs/en/workspace/files.md", "checks the **target** limits"),
        new("При переносе товара между магазинами лимиты файлов проверяются у источника или у цели?",
            "product-docs/ru/workspace/files.md", "проверяет лимиты **цели**"),
        new("If I download the same photo URL twice, can it create another file copy?",
            "product-docs/en/workspace/files.md", "may create another copy"),
        new("Что будет с копиями файлов при повторной загрузке одного и того же URL?",
            "product-docs/ru/workspace/files.md", "может создать ещё одну копию"),
    ];

    [Fact]
    public async Task RepositoryDocs_CompareLexicalAndOptInLocalEmbeddings()
    {
        await using var db = TestDbFactory.CreateDb();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance);
        // Same local-file enumeration/chunking as the production index; no DB writes,
        // user files, connected marketplace, or chat-provider calls.
        var corpus = index.LoadFromDisk().ToArray();
        var cases = Cases;
        if (string.Equals(Environment.GetEnvironmentVariable("AI_KNOWLEDGE_PUBLIC_DOCS_EVAL"),
                "true", StringComparison.OrdinalIgnoreCase))
        {
            var snapshotPath = Environment.GetEnvironmentVariable("AI_KNOWLEDGE_EVAL_SNAPSHOT");
            if (!string.IsNullOrWhiteSpace(snapshotPath) && System.IO.File.Exists(snapshotPath))
            {
                corpus = JsonSerializer.Deserialize<KnowledgeChunk[]>(
                    await System.IO.File.ReadAllTextAsync(snapshotPath))
                    ?? throw new InvalidDataException("The frozen corpus must contain a chunk array.");
                output.WriteLine("Using frozen combined corpus: " + snapshotPath);
            }
            else
            {
                corpus = corpus.Concat(await LoadPublicDocsAsync()).ToArray();
                if (!string.IsNullOrWhiteSpace(snapshotPath))
                {
                    // Never replace an existing snapshot: comparisons need identical inputs.
                    await using var snapshot = new FileStream(snapshotPath, FileMode.CreateNew);
                    await JsonSerializer.SerializeAsync(snapshot, corpus);
                    output.WriteLine("Saved frozen combined corpus: " + snapshotPath);
                }
            }
            cases = Cases.Concat(PublicCases).ToArray();
            if (string.Equals(Environment.GetEnvironmentVariable("AI_KNOWLEDGE_EVAL_HOLDOUT"),
                    "true", StringComparison.OrdinalIgnoreCase))
                cases = cases.Concat(ValidationCases).ToArray();
        }
        Assert.NotEmpty(corpus);
        foreach (var item in cases)
        {
            Assert.Contains(corpus, c => c.SourcePath == item.Source
                && c.Body.Contains(item.Evidence, StringComparison.OrdinalIgnoreCase));
            foreach (var alternative in item.Alternatives)
                Assert.Contains(corpus, c => c.SourcePath == alternative.Source
                    && c.Body.Contains(alternative.Text, StringComparison.OrdinalIgnoreCase));
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(corpus))));
        output.WriteLine($"Corpus: {corpus.Length} chunks, SHA256={fingerprint}");

        var embeddingTitles = corpus.Select(chunk => chunk.Title).ToArray();
        if (string.Equals(Environment.GetEnvironmentVariable("AI_KNOWLEDGE_EVAL_TITLE_CONTEXT"),
                "true", StringComparison.OrdinalIgnoreCase))
        {
            var titles = corpus.GroupBy(chunk => chunk.SourcePath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Title, StringComparer.Ordinal);
            embeddingTitles = corpus.Select(chunk => titles[chunk.SourcePath] == chunk.Title
                ? chunk.Title : $"{titles[chunk.SourcePath]} — {chunk.Title}").ToArray();
            output.WriteLine("Experimental embedding title context: document title + section; "
                + "retrieval titles and bodies unchanged.");
        }

        // Compare the first three hits and the actual search_knowledge default (12).
        var lexical = cases.Select(item => KnowledgeSearch.Search(corpus, item.Query, 12)).ToArray();
        PrintGroups("Lexical", lexical, cases);

        if (!string.Equals(Environment.GetEnvironmentVariable("AI_KNOWLEDGE_REAL_DOCS_EVAL"),
                "true", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("Local embeddings: not measured (real-docs evaluation disabled).");
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var model = new KnowledgeEmbeddingClient(http,
            new ConfigurationBuilder().AddEnvironmentVariables().Build());
        Assert.True(model.IsEnabled, "Enable the local model and set AI_KNOWLEDGE_OLLAMA_URL");
        var timer = Stopwatch.StartNew();
        var vectors = new List<float[]?>();
        foreach (var batch in Enumerable.Range(0, corpus.Length).Chunk(8))
        {
            var embedded = await model.EmbedAsync(batch.Select(i =>
                KnowledgeEmbeddingInputs.Document(embeddingTitles[i], corpus[i].Body, model.ModelId))
                .ToArray(), default);
            vectors.AddRange(embedded.Select(vector => (float[]?)vector));
        }
        output.WriteLine($"Local indexing: {corpus.Length} chunks, {timer.ElapsedMilliseconds} ms, "
            + $"vector dimensions={vectors[0]?.Length}");

        var hybrid = new List<IReadOnlyList<KnowledgeChunk>>();
        var times = new List<long>();
        foreach (var item in cases)
        {
            timer.Restart();
            var queryVector = Assert.Single(await model.EmbedAsync(
                [KnowledgeEmbeddingInputs.Query(item.Query, model.ModelId)], default));
            hybrid.Add(KnowledgeSearch.Hybrid(corpus, item.Query, vectors, queryVector, 12));
            times.Add(timer.ElapsedMilliseconds);
            if (!hybrid[^1].Any(hit => hit.SourcePath == item.Source
                    && hit.Body.Contains(item.Evidence, StringComparison.OrdinalIgnoreCase)))
            {
                var semantic = corpus.Select((chunk, i) => (chunk, score: Cosine(queryVector, vectors[i])))
                    .OrderByDescending(hit => hit.score).ToArray();
                var goldRank = Array.FindIndex(semantic, hit => hit.chunk.SourcePath == item.Source
                    && hit.chunk.Body.Contains(item.Evidence, StringComparison.OrdinalIgnoreCase)) + 1;
                output.WriteLine($"Miss diagnostic: '{item.Query}', gold semantic rank={goldRank}");
                foreach (var hit in hybrid[^1].Take(3))
                    output.WriteLine($"  Returned {hit.SourcePath} / {hit.Title}: {hit.Body}");
                var gold = semantic.First(hit => hit.chunk.SourcePath == item.Source
                    && hit.chunk.Body.Contains(item.Evidence, StringComparison.OrdinalIgnoreCase));
                output.WriteLine($"  Gold {gold.chunk.SourcePath} / {gold.chunk.Title} "
                    + $"(cosine={gold.score:F3}): {gold.chunk.Body}");
            }
        }
        PrintGroups("Hybrid", hybrid, cases);
        times.Sort();
        output.WriteLine($"Observed query latency (local embedding + in-process ranking), "
            + $"p50={times[(times.Count - 1) / 2]} ms, "
            + $"p95={times[(int)Math.Ceiling(times.Count * 0.95) - 1]} ms; "
            + $"{cases.Length} queries, not a production latency estimate");

        foreach (var query in OutOfScopeQueries)
        {
            var queryVector = Assert.Single(await model.EmbedAsync(
                [KnowledgeEmbeddingInputs.Query(query, model.ModelId)], default));
            var nearest = corpus.Select((chunk, i) =>
                    (chunk, score: Cosine(queryVector, vectors[i])))
                .OrderByDescending(item => item.score).First();
            var hits = KnowledgeSearch.Hybrid(corpus, query, vectors, queryVector, 3);
            var lexicalHits = KnowledgeSearch.Search(corpus, query, 3);
            output.WriteLine($"Out-of-scope '{query}' => nearest cosine={nearest.score:F3} "
                + $"({nearest.chunk.SourcePath}), lexical={lexicalHits.Count}, hybrid={hits.Count} ["
                + $"{string.Join(", ", hits.Select(hit => hit.SourcePath))}]");
        }
    }

    private void PrintGroups(string name, IReadOnlyList<IReadOnlyList<KnowledgeChunk>> results,
        IReadOnlyList<Case> cases)
    {
        if (cases.Count <= 18)
        {
            PrintResults(name, results, cases);
            return;
        }
        PrintResults(name, results.Take(18).ToArray(), cases.Take(18).ToArray());
        PrintResults(name + " validation", results.Skip(18).ToArray(), cases.Skip(18).ToArray());
    }

    private void PrintResults(string name, IReadOnlyList<IReadOnlyList<KnowledgeChunk>> results,
        IReadOnlyList<Case> cases)
    {
        var sourceAtOne = 0;
        var sourceAtThree = 0;
        var sourceAtTwelve = 0;
        var evidenceAtOne = 0;
        var evidenceAtThree = 0;
        var evidenceAtTwelve = 0;
        var supportedAtOne = 0;
        var supportedAtThree = 0;
        var supportedAtTwelve = 0;
        var maxToolJsonChars = 0;
        for (var i = 0; i < cases.Count; i++)
        {
            var item = cases[i];
            var hits = results[i];
            bool IsSource(KnowledgeChunk hit) => hit.SourcePath == item.Source;
            bool HasEvidence(KnowledgeChunk hit) => IsSource(hit)
                && KnowledgeSearch.Snippet(hit, item.Query).Contains(
                    item.Evidence, StringComparison.OrdinalIgnoreCase);
            bool HasSupportedAnswer(KnowledgeChunk hit) => HasEvidence(hit)
                || item.Alternatives.Any(alternative => hit.SourcePath == alternative.Source
                    && KnowledgeSearch.Snippet(hit, item.Query).Contains(
                        alternative.Text, StringComparison.OrdinalIgnoreCase));
            if (hits.FirstOrDefault() is { } first)
            {
                if (IsSource(first)) sourceAtOne++;
                if (HasEvidence(first)) evidenceAtOne++;
                if (HasSupportedAnswer(first)) supportedAtOne++;
            }
            if (hits.Take(3).Any(IsSource)) sourceAtThree++;
            if (hits.Any(IsSource)) sourceAtTwelve++;
            if (hits.Take(3).Any(HasEvidence)) evidenceAtThree++;
            if (hits.Any(HasEvidence)) evidenceAtTwelve++;
            if (hits.Take(3).Any(HasSupportedAnswer)) supportedAtThree++;
            if (hits.Any(HasSupportedAnswer)) supportedAtTwelve++;
            var toolJson = JsonSerializer.Serialize(new
            {
                items = hits.Select(hit => new
                {
                    hit.SourcePath, hit.Title,
                    snippet = KnowledgeSearch.Snippet(hit, item.Query),
                }),
            }, ModelToolJson);
            maxToolJsonChars = Math.Max(maxToolJsonChars, toolJson.Length);
            Assert.Equal(toolJson, AgentToolResult.TruncateForModel(toolJson));
            output.WriteLine($"{name} {i + 1}: source={item.Source}, "
                + $"first3=[{string.Join(", ", hits.Take(3).Select(c => c.SourcePath))}], "
                + $"evidenceRank={Array.FindIndex(hits.ToArray(), c => HasEvidence(c)) + 1}, "
                + $"reviewedAnswerRank={Array.FindIndex(hits.ToArray(), c => HasSupportedAnswer(c)) + 1}");
        }
        output.WriteLine($"{name} source recall@1/@3/@12: {sourceAtOne}/{sourceAtThree}/"
            + $"{sourceAtTwelve} of {cases.Count}; evidence in model-visible snippet "
            + $"@1/@3/@12: {evidenceAtOne}/{evidenceAtThree}/{evidenceAtTwelve} "
            + $"of {cases.Count}");
        output.WriteLine($"{name} longest serialized 12-hit tool result: {maxToolJsonChars} "
            + $"of {AgentToolResult.MaxModelResultChars} characters");
        output.WriteLine($"{name} reviewed answer evidence (including explicit alternatives) "
            + $"@1/@3/@12: {supportedAtOne}/{supportedAtThree}/{supportedAtTwelve} of {cases.Count}");
    }

    private async Task<IReadOnlyList<KnowledgeChunk>> LoadPublicDocsAsync()
    {
        // Explicitly opt-in public GETs only; use the application's URL/slug/redirect policy.
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddHttpClient(PublicDocsClient.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        using var provider = services.BuildServiceProvider();
        var docs = new PublicDocsClient(provider.GetRequiredService<IHttpClientFactory>(),
            new PublicDocsOptions(), provider.GetRequiredService<IMemoryCache>(),
            NullLogger<PublicDocsClient>.Instance);
        var chunks = new List<KnowledgeChunk>();
        var pageCount = 0;
        var unavailable = new List<string>();
        foreach (var locale in new[] { "ru", "en" })
        {
            var catalog = await docs.GetCatalogAsync(locale);
            foreach (var slug in catalog.Groups.SelectMany(group => group.Items)
                         .Select(item => item.Slug ?? "").Distinct(StringComparer.Ordinal))
            {
                DocsPage page;
                try { page = await docs.GetPageAsync(slug, locale); }
                catch (DocsClientException ex)
                {
                    unavailable.Add($"{locale}/{slug}: {ex.Code}");
                    continue;
                }
                var path = string.IsNullOrEmpty(slug) ? $"product-docs/{locale}/index.md"
                    : $"product-docs/{locale}/{slug}.md";
                chunks.AddRange(KnowledgeChunker.Chunk(path, page.Markdown));
                pageCount++;
            }
        }
        output.WriteLine($"Public docs: {pageCount} pages, {chunks.Count} chunks from "
            + PublicDocsOptions.DefaultBaseUrl);
        output.WriteLine($"Unavailable catalog entries (no stored snapshot in this test): "
            + string.Join(", ", unavailable));
        return chunks;
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float>? right)
    {
        if (right == null || left.Count != right.Count) return 0;
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var i = 0; i < left.Count; i++)
        {
            dot += left[i] * right[i];
            leftLength += left[i] * left[i];
            rightLength += right[i] * right[i];
        }
        return leftLength > 0 && rightLength > 0
            ? dot / Math.Sqrt(leftLength * rightLength) : 0;
    }
}
