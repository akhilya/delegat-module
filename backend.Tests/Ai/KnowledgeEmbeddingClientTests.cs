using System.Net;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.Configuration;

namespace Delegat.Tests;

public class KnowledgeEmbeddingClientTests
{
    [Fact]
    public void RetrievalPrompts_FollowModelInstructionsWithoutChangingOtherModels()
    {
        Assert.Equal("task: search result | query: вопрос",
            KnowledgeEmbeddingInputs.Query("вопрос", "embeddinggemma:latest"));
        Assert.Equal("title: Help | text: Answer",
            KnowledgeEmbeddingInputs.Document("Help", "Answer", "embeddinggemma"));
        Assert.Equal("Help\nAnswer",
            KnowledgeEmbeddingInputs.Document("Help", "Answer", "other-model"));
    }

    [Fact]
    public async Task LocalOllama_EmbedsBatchWithoutTruncatingSource()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var model = new KnowledgeEmbeddingClient(http, Settings("http://ollama:11434"));

        var vectors = await model.EmbedAsync(["A", "B"], default);

        Assert.Equal(2, vectors.Count);
        Assert.Equal("http://ollama:11434/api/embed", handler.Uri?.ToString());
        Assert.Contains("\"truncate\":false", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"model\":\"embeddinggemma\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteOrigin_CannotReceiveKnowledgeText()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var model = new KnowledgeEmbeddingClient(http, Settings("https://embeddings.example.org"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.EmbedAsync(["private docs"], default));
        Assert.Null(handler.Uri);
    }

    private static IConfiguration Settings(string url) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI_KNOWLEDGE_VECTOR_ENABLED"] = "true",
            ["AI_KNOWLEDGE_OLLAMA_URL"] = url,
        }).Build();

    private sealed class StubHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"embeddings":[[1,0],[0,1]]}"""),
            };
        }
    }
}
