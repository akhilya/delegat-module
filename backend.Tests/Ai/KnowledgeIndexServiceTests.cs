using Delegat.Services.Ai.Agent;
using Delegat.Services.Docs;
using Delegat.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class KnowledgeIndexServiceTests
{
    [Fact]
    public async Task ReindexAsync_FetchesProductDocsIntoChunks()
    {
        await using var db = TestDbFactory.CreateDb();
        var docs = new StubPublicDocs();
        var index = new KnowledgeIndexService(
            db,
            new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance,
            env: null,
            publicDocs: docs);

        await index.ReindexAsync();

        Assert.Contains(db.aiKnowledgeChunks, c => c.sourcePath == "product-docs/ru/workspace/products.md");
        Assert.Contains(db.aiKnowledgeChunks, c => c.sourcePath == "product-docs/en/workspace/products.md");
        Assert.Contains(db.aiKnowledgeChunks, c => c.body.Contains("Add SKUs"));
        Assert.DoesNotContain(db.aiKnowledgeChunks, c =>
            c.sourcePath.Replace('\\', '/').StartsWith("docs/plans/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReindexAsync_RetainsIndexedDocsWhenRemoteCatalogTemporarilyFails()
    {
        await using var db = TestDbFactory.CreateDb();
        var docs = new StubPublicDocs();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, publicDocs: docs);

        await index.ReindexAsync();
        docs.FailCatalog = true;
        await index.ReindexAsync();

        Assert.Contains(db.aiKnowledgeChunks, c =>
            c.sourcePath == "product-docs/en/workspace/products.md"
            && c.body.Contains("Add SKUs", StringComparison.Ordinal));
        Assert.Contains(await index.SearchAsync("Add SKUs", 5, default),
            c => c.SourcePath == "product-docs/en/workspace/products.md");
    }

    [Fact]
    public async Task ReindexAsync_RemovesStaleDocsWhenRemoteCatalogSuccessfullyRemovesThem()
    {
        await using var db = TestDbFactory.CreateDb();
        var docs = new StubPublicDocs();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, publicDocs: docs);

        await index.ReindexAsync();
        docs.EmptyCatalog = true;
        await index.ReindexAsync();

        Assert.DoesNotContain(db.aiKnowledgeChunks, c => c.sourcePath.StartsWith("product-docs/"));
    }

    [Fact]
    public async Task ReindexAsync_RetainsOnlyTheRemotePageThatFailed()
    {
        await using var db = TestDbFactory.CreateDb();
        var docs = new StubPublicDocs();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, publicDocs: docs);

        await index.ReindexAsync();
        docs.FailPageLocale = "en";
        docs.NewBody = "Newly updated content.";
        await index.ReindexAsync();

        Assert.Contains(db.aiKnowledgeChunks, c => c.sourcePath == "product-docs/en/workspace/products.md"
            && c.body.Contains("Add SKUs", StringComparison.Ordinal));
        Assert.Contains(db.aiKnowledgeChunks, c => c.sourcePath == "product-docs/ru/workspace/products.md"
            && c.body.Contains("Newly updated content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReindexAsync_MakesEndOfLongRemotePageSearchableFromDatabase()
    {
        await using var db = TestDbFactory.CreateDb();
        var docs = new StubPublicDocs
        {
            NewBody = new string('a', 4_500) + "\nFulfillment reconciliation checkpoint.",
        };
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, publicDocs: docs);

        await index.ReindexAsync();
        var hits = await index.SearchAsync("reconciliation checkpoint", 5, default);

        Assert.Contains(hits, hit => hit.SourcePath == "product-docs/en/workspace/products.md"
            && hit.Body.Contains("Fulfillment reconciliation checkpoint", StringComparison.Ordinal));
        Assert.All(db.aiKnowledgeChunks, c => Assert.InRange(c.body.Length, 0, 900));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadSourceAsync_RestoresHeadingsWithoutRepeatingThemPerContinuation(bool documentHeading)
    {
        await using var db = TestDbFactory.CreateDb();
        const string path = "docs/transfer-guide.md";
        var table = "| Item | Result |\n| --- | --- |\n"
            + string.Concat(Enumerable.Range(0, 80).Select(i => $"| item-{i} | pending |\n"));
        var markdown = (documentHeading ? "# Transfer guide\n\nIntroduction.\n\n" : "")
            + "## Documented rules\n\n" + new string('x', 1_850)
            + "\n\n### Results\n\n" + table.TrimEnd()
            + "\n\n## Current configuration\n\nA disabled helper does not redefine role permissions."
            + "\n\n## Documented rules\n\nAn independent section with the same title.";
        var chunks = KnowledgeChunker.Chunk(path, markdown);
        Assert.True(chunks.Count > 5);
        db.aiKnowledgeChunks.AddRange(chunks.Select(chunk => new Delegat.Models.Ai.AiKnowledgeChunk
        {
            sourcePath = chunk.SourcePath, title = chunk.Title, body = chunk.Body,
        }));
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance);

        var document = await index.ReadSourceAsync(path, "en", default);

        Assert.NotNull(document);
        Assert.Equal(2, document.Body.Split("## Documented rules", StringSplitOptions.None).Length - 1);
        Assert.Contains("## Current configuration\n\nA disabled helper", document.Body, StringComparison.Ordinal);
        Assert.Contains("### Results", document.Body, StringComparison.Ordinal);
        Assert.Contains("| item-79 | pending |", document.Body, StringComparison.Ordinal);
        Assert.EndsWith("An independent section with the same title.", document.Body, StringComparison.Ordinal);
        Assert.Equal(documentHeading ? 1 : 0,
            document.Body.Split("# Transfer guide", StringSplitOptions.None).Length - 1);
        Assert.Equal(chunks[0].Title, document.Title);
    }

    [Fact]
    public async Task ReadSourceAsync_HeadinglessDocumentDoesNotInventAHeading()
    {
        await using var db = TestDbFactory.CreateDb();
        const string path = "knowledge/notes.md";
        var markdown = string.Concat(Enumerable.Repeat("An uninterrupted sentence. ", 100)).TrimEnd();
        var chunks = KnowledgeChunker.Chunk(path, markdown);
        db.aiKnowledgeChunks.AddRange(chunks
            .Select(chunk => new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = chunk.SourcePath, title = chunk.Title, body = chunk.Body,
            }));
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance);

        var document = await index.ReadSourceAsync(path, "en", default);

        Assert.NotNull(document);
        Assert.DoesNotContain("## notes.md", document.Body, StringComparison.Ordinal);
        Assert.Equal(string.Join("\n\n", chunks.Select(chunk => chunk.Body)), document.Body);
    }

    [Fact]
    public async Task ReadSourceAsync_DoesNotMergeAdjacentSameTitleSections()
    {
        await using var db = TestDbFactory.CreateDb();
        const string path = "docs/repeated.md";
        const string markdown = "## Notes\n\nFirst paragraph.\n\n## Notes\n\nSecond paragraph.";
        db.aiKnowledgeChunks.AddRange(KnowledgeChunker.Chunk(path, markdown)
            .Select(chunk => new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = chunk.SourcePath, title = chunk.Title, body = chunk.Body,
            }));
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance);

        var document = await index.ReadSourceAsync(path, "en", default);

        Assert.NotNull(document);
        Assert.Contains("First paragraph.\n\nSecond paragraph.", document.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadSourceAsync_ReassembledDocumentRetainsTheSizeLimit()
    {
        await using var db = TestDbFactory.CreateDb();
        const string path = "docs/large.md";
        var markdown = "# Large\n\n## Details\n\n" + new string('x', 33_000);
        db.aiKnowledgeChunks.AddRange(KnowledgeChunker.Chunk(path, markdown)
            .Select(chunk => new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = chunk.SourcePath, title = chunk.Title, body = chunk.Body,
            }));
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance);

        var document = await index.ReadSourceAsync(path, "en", default);

        Assert.NotNull(document);
        Assert.Equal(32_000, document.Body.Length);
        Assert.StartsWith("# Large\n\n## Details\n\n", document.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalEmbeddings_ArePersistedReusedAndFallBackWhenUnavailable()
    {
        await using var db = TestDbFactory.CreateDb();
        var embeddings = new StubEmbeddings();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, publicDocs: new StubPublicDocs(),
            embeddings: embeddings);

        await index.ReindexAsync();
        await index.UpdateEmbeddingsAsync();
        var indexed = db.aiKnowledgeChunks
            .Where(c => c.sourcePath == "product-docs/en/workspace/products.md").ToList();
        Assert.All(indexed, c => Assert.Equal(embeddings.ModelId, c.embeddingModel));
        Assert.All(indexed, c => Assert.NotNull(c.embeddingJson));
        var batchCalls = embeddings.Calls;

        await index.ReindexAsync();
        await index.UpdateEmbeddingsAsync();
        Assert.Equal(batchCalls, embeddings.Calls);

        embeddings.ModelId = "test-new-model";
        await index.UpdateEmbeddingsAsync();
        Assert.True(embeddings.Calls > batchCalls);
        Assert.Contains(db.aiKnowledgeChunks, c => c.embeddingModel == "test-new-model");

        embeddings.Fail = true;
        Assert.Contains(await index.SearchAsync("Add SKUs", 5, default),
            c => c.SourcePath == "product-docs/en/workspace/products.md");
    }

    [Fact]
    public async Task HybridSearch_UsesPersistedVectorsForAQueryWithNoLiteralMatch()
    {
        await using var db = TestDbFactory.CreateDb();
        db.aiKnowledgeChunks.AddRange(
            new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = "docs/mapping.md", title = "Field mappings",
                body = "Associate each shop field with a local attribute.",
            },
            new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = "docs/export.md", title = "Publish products",
                body = "Send existing catalog cards to a connected storefront.",
            });
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, embeddings: new SelectiveEmbeddings());

        Assert.Empty(await index.SearchAsync("refresh seller listings", 1, default));
        await index.UpdateEmbeddingsAsync();

        Assert.Equal("docs/export.md", Assert.Single(
            await index.SearchAsync("refresh seller listings", 1, default)).SourcePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LexicalSearch_WithPersistedVectors_DoesNotCallEmbeddings(bool configureDisabledClient)
    {
        await using var db = TestDbFactory.CreateDb();
        var embeddings = new StubEmbeddings { IsEnabled = false };
        db.aiKnowledgeChunks.AddRange(
            new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = "docs/exports.md", title = "Export conditions", body = "A transfer can remain pending.",
                embeddingModel = embeddings.ModelId, embeddingJson = "[1,0]",
            },
            new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = "docs/other.md", title = "Unrelated", body = "Another article.",
                embeddingModel = embeddings.ModelId, embeddingJson = "not a valid vector",
            });
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance,
            embeddings: configureDisabledClient ? embeddings : null);

        var hits = await index.SearchAsync("Export conditions", 12, default);
        var document = await index.ReadSourceAsync("docs/exports.md", "en", default);

        Assert.Equal("docs/exports.md", Assert.Single(hits).SourcePath);
        Assert.NotNull(document);
        Assert.Contains("A transfer can remain pending.", document.Body, StringComparison.Ordinal);
        Assert.Equal(0, embeddings.Calls);
    }

    [Fact]
    public async Task LocalIndex_OmitsDatedAuditsAndEvaluationProtocol()
    {
        await using var db = TestDbFactory.CreateDb();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance);

        var paths = index.EnumerateCanonFiles().Select(file => file.RelPath).ToArray();

        Assert.Contains("docs/architecture/authentication.md", paths);
        Assert.DoesNotContain(paths, path => path.StartsWith("docs/plans/", StringComparison.Ordinal)
            || path == "docs/security/SECURITY_AUDIT.md"
            || path == "docs/security/SECURITY_AUDIT_REPORT.md"
            || path == "docs/architecture/ai-agent-audit.md"
            || path == "docs/ai/assistant-quality-evaluation.md");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmbeddingTimeout_KeepsLexicalSearchAvailable(bool duringIndexing)
    {
        await using var db = TestDbFactory.CreateDb();
        var embeddings = new StubEmbeddings { Timeout = true };
        db.aiKnowledgeChunks.Add(new Delegat.Models.Ai.AiKnowledgeChunk
        {
            sourcePath = "docs/export.md", title = "Export products", body = "Publish existing cards.",
            embeddingModel = duringIndexing ? null : embeddings.ModelId,
            embeddingJson = duringIndexing ? null : "[1,0]",
        });
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, embeddings: embeddings);

        if (duringIndexing) await index.UpdateEmbeddingsAsync();
        var hits = await index.SearchAsync("Export products", 3, default);

        Assert.Equal("docs/export.md", Assert.Single(hits).SourcePath);
        Assert.Equal(1, embeddings.Calls);
        if (duringIndexing)
            Assert.Null(Assert.Single(db.aiKnowledgeChunks).embeddingJson);
    }

    [Fact]
    public async Task QueryEmbeddingBudget_CancelsOnlyTheEmbeddingAndReturnsLexicalResults()
    {
        await using var db = TestDbFactory.CreateDb();
        var embeddings = new WaitingEmbeddings();
        db.aiKnowledgeChunks.Add(new Delegat.Models.Ai.AiKnowledgeChunk
        {
            sourcePath = "docs/export.md", title = "Export products", body = "Publish existing cards.",
            embeddingModel = embeddings.ModelId, embeddingJson = "[1,0]",
        });
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, embeddings: embeddings);

        var hits = await index.SearchAsync("Export products", 3, default);

        Assert.Equal("docs/export.md", Assert.Single(hits).SourcePath);
        Assert.True(embeddings.Token.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_DuringEmbedding_IsNotSwallowed(bool duringIndexing)
    {
        await using var db = TestDbFactory.CreateDb();
        using var cancellation = new CancellationTokenSource();
        var embeddings = new StubEmbeddings
        {
            BeforeEmbed = _ => cancellation.Cancel(),
        };
        db.aiKnowledgeChunks.Add(new Delegat.Models.Ai.AiKnowledgeChunk
        {
            sourcePath = "docs/export.md", title = "Export products", body = "Publish existing cards.",
            embeddingModel = duringIndexing ? null : embeddings.ModelId,
            embeddingJson = duringIndexing ? null : "[1,0]",
        });
        await db.SaveChangesAsync();
        var index = new KnowledgeIndexService(db, new FakeStorage(),
            NullLogger<KnowledgeIndexService>.Instance, embeddings: embeddings);

        if (duringIndexing)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                index.UpdateEmbeddingsAsync(cancellation.Token));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                index.SearchAsync("Export products", 3, cancellation.Token));
        Assert.Equal(1, embeddings.Calls);
    }

    private sealed class StubPublicDocs : IPublicDocsClient
    {
        public bool FailCatalog { get; set; }
        public bool EmptyCatalog { get; set; }
        public string? FailPageLocale { get; set; }
        public string NewBody { get; set; } = "Add SKUs.";

        public Task<DocsCatalog> GetCatalogAsync(string locale, CancellationToken ct = default)
        {
            if (FailCatalog) throw new DocsClientException("docs_unavailable", 503, "unavailable");
            return Task.FromResult(new DocsCatalog
            {
                Locale = locale,
                Groups = EmptyCatalog ? [] :
                [
                    new DocsCatalogGroup
                    {
                        Text = "Workspace",
                        Items =
                        [
                            new DocsCatalogItem { Text = "Products", Slug = "workspace/products" },
                        ],
                    },
                ],
            });
        }

        public Task<DocsPage> GetPageAsync(string slug, string locale, CancellationToken ct = default)
        {
            if (slug != "workspace/products")
                throw new DocsClientException("docs_not_found", 404, "missing");
            if (locale == FailPageLocale)
                throw new DocsClientException("docs_unavailable", 503, "unavailable");
            var title = locale == "en" ? "Products" : "Товары";
            return Task.FromResult(new DocsPage
            {
                Slug = slug,
                Locale = locale,
                Title = title,
                Markdown = $"# {title}\n{NewBody}",
            });
        }
    }

    private sealed class StubEmbeddings : IKnowledgeEmbeddingClient
    {
        public bool IsEnabled { get; set; } = true;
        public string ModelId { get; set; } = "test-local-model";
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public bool Timeout { get; set; }
        public Action<CancellationToken>? BeforeEmbed { get; set; }
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Calls++;
            BeforeEmbed?.Invoke(ct);
            ct.ThrowIfCancellationRequested();
            if (Timeout) throw new TaskCanceledException("local HTTP timeout", new TimeoutException());
            if (Fail) throw new HttpRequestException("local model unavailable");
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 1, 0 }).ToArray());
        }
    }

    private sealed class SelectiveEmbeddings : IKnowledgeEmbeddingClient
    {
        public bool IsEnabled => true;
        public string ModelId => "test-selective-model";
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(text =>
                text.Contains("Field mappings", StringComparison.Ordinal)
                     ? new float[] { 0, 1 } : new float[] { 1, 0 }).ToArray());
    }

    private sealed class WaitingEmbeddings : IKnowledgeEmbeddingClient
    {
        public bool IsEnabled => true;
        public string ModelId => "waiting-model";
        public CancellationToken Token { get; private set; }
        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Token = ct;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return [];
        }
    }
}
