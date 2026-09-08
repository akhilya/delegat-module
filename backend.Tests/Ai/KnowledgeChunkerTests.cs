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
}
