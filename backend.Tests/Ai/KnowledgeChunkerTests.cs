using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class KnowledgeChunkerTests
{
    [Fact]
    public void Splits_MarkdownByHeadingsAndScoresQuery()
    {
        var md = """
            # Title

            Intro paragraph about MCP tools.

            ## SQL

            Read-only SELECT against the store slice.
            """;

        var chunks = KnowledgeChunker.Chunk("docs/mcp.md", md);
        Assert.True(chunks.Count >= 2);
        Assert.Contains(chunks, c => c.Title.Contains("SQL", StringComparison.OrdinalIgnoreCase));

        var hits = KnowledgeSearch.Search(chunks, "read-only SELECT store", take: 5);
        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Body.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        Assert.Empty(KnowledgeSearch.Search(
            [new KnowledgeChunk("a.md", "A", "hello world")],
            "   ",
            5));
    }

    [Fact]
    public void LongSection_KeepsEndOfDocumentSearchable()
    {
        var text = "# Operations\n\n## Exports\n" + new string('a', 4_500)
            + "\nThe fulfillment reconciliation checkpoint is mandatory.";

        var chunks = KnowledgeChunker.Chunk("docs/operations.md", text);

        Assert.All(chunks, chunk => Assert.InRange(chunk.Body.Length, 1, 900));
        Assert.Contains(KnowledgeSearch.Search(chunks, "reconciliation checkpoint", 5),
            chunk => chunk.Body.Contains("fulfillment reconciliation checkpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void SearchSnippet_ShowsMatchingPassageInsteadOfUnrelatedBeginning()
    {
        var body = new string('a', 1_100) + " The permission checkpoint is mandatory.";
        var chunk = new KnowledgeChunk("docs/security.md", "Permissions", body);

        var snippet = KnowledgeSearch.Snippet(chunk, "checkpoint", 900);

        Assert.Contains("permission checkpoint is mandatory", snippet, StringComparison.Ordinal);
        Assert.InRange(snippet.Length, 1, 903);
    }

    [Fact]
    public void Search_QueryPunctuationDoesNotHideARelevantDocument()
    {
        var chunks = new[] { new KnowledgeChunk("docs/orders.md", "Orders", "Check shipment status.") };
        Assert.Single(KnowledgeSearch.Search(chunks, "status?", 5));
    }
}
