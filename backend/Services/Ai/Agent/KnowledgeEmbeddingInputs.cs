namespace Delegat.Services.Ai.Agent;

/// <summary>Retrieval input formatting for models that require asymmetric prompts.</summary>
public static class KnowledgeEmbeddingInputs
{
    public static string Query(string text, string modelId) => IsEmbeddingGemma(modelId)
        ? $"task: search result | query: {text}"
        : text;

    public static string Document(string title, string body, string modelId) => IsEmbeddingGemma(modelId)
        ? $"title: {title} | text: {body}"
        : $"{title}\n{body}";

    private static bool IsEmbeddingGemma(string modelId) =>
        modelId.Equals("embeddinggemma", StringComparison.OrdinalIgnoreCase)
        || modelId.StartsWith("embeddinggemma:", StringComparison.OrdinalIgnoreCase);
}
