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

    private sealed class StubPublicDocs : IPublicDocsClient
    {
        public Task<DocsCatalog> GetCatalogAsync(string locale, CancellationToken ct = default) =>
            Task.FromResult(new DocsCatalog
            {
                Locale = locale,
                Groups =
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

        public Task<DocsPage> GetPageAsync(string slug, string locale, CancellationToken ct = default)
        {
            if (slug != "workspace/products")
                throw new DocsClientException("docs_not_found", 404, "missing");
            var title = locale == "en" ? "Products" : "Товары";
            return Task.FromResult(new DocsPage
            {
                Slug = slug,
                Locale = locale,
                Title = title,
                Markdown = $"# {title}\nAdd SKUs.",
            });
        }
    }
}
