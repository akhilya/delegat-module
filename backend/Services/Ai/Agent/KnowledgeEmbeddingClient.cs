using System.Net.Http.Json;
using System.Text.Json;

namespace Delegat.Services.Ai.Agent;

/// <summary>Optional local-only embedding source for the documentation index.</summary>
public interface IKnowledgeEmbeddingClient
{
    bool IsEnabled { get; }
    string ModelId { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct);
}

public sealed class KnowledgeEmbeddingClient(HttpClient http, IConfiguration config) : IKnowledgeEmbeddingClient
{
    private readonly string? _url = config["AI_KNOWLEDGE_OLLAMA_URL"];
    public string ModelId { get; } = config["AI_KNOWLEDGE_EMBEDDING_MODEL"] ?? "embeddinggemma";
    public bool IsEnabled => ModelId.Length is > 0 and <= 200 && string.Equals(
        config["AI_KNOWLEDGE_VECTOR_ENABLED"], "true", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(_url);

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (!IsEnabled || texts.Count == 0) return [];
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttp
            || (endpoint.Host != "ollama" && endpoint.Host != "localhost"
                && endpoint.Host != "127.0.0.1" && endpoint.Host != "::1"))
            throw new InvalidOperationException("Knowledge embeddings require a local Ollama URL");

        using var response = await http.PostAsJsonAsync(new Uri(endpoint, "/api/embed"),
            new { model = ModelId, input = texts, truncate = false }, ct);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct);
        var embeddings = json.RootElement.GetProperty("embeddings").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(value => value.GetSingle()).ToArray()).ToArray();
        if (embeddings.Length != texts.Count || embeddings.Select(v => v.Length).Distinct().Count() != 1
            || embeddings.Any(v => v.Length is < 1 or > 4096
                || v.Any(value => !float.IsFinite(value))))
            throw new InvalidOperationException("Invalid knowledge embedding response");
        return embeddings;
    }
}
