using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;

namespace Delegat.Services.Ai;

public class AiModelConfigService
{
    private readonly DataContext _context;
    private readonly EncryptionService _encryption;
    private readonly AiApiClient _apiClient;

    public AiModelConfigService(DataContext context, AiApiClient apiClient, EncryptionService encryption)
    {
        _context = context;
        _encryption = encryption;
        _apiClient = apiClient;
    }

    /// <summary>First enabled model; prefers the one marked as default.</summary>
    public async Task<AiModelConfig?> GetDefaultModelAsync(CancellationToken ct = default)
        => await _context.aiModelConfigs.AsNoTracking()
            .Where(m => m.isEnabled)
            .OrderByDescending(m => m.isDefault)
            .ThenBy(m => m.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>Resolve an enabled model: explicit id (if enabled) or default enabled model.</summary>
    public async Task<AiModelConfig?> ResolveEnabledModelAsync(int? modelId, CancellationToken ct = default)
    {
        if (modelId.HasValue)
        {
            var explicitModel = await _context.aiModelConfigs.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == modelId.Value && m.isEnabled, ct);
            if (explicitModel != null)
                return explicitModel;
        }

        return await GetDefaultModelAsync(ct);
    }

    public async Task<bool> HasEnabledModelsAsync(CancellationToken ct = default)
        => await _context.aiModelConfigs.AsNoTracking().AnyAsync(m => m.isEnabled, ct);

    public async Task<List<AiModelConfig>> GetAllAsync(CancellationToken ct = default)
        => await _context.aiModelConfigs.AsNoTracking().OrderBy(m => m.createdAt).ToListAsync(ct);

    public async Task<AiModelConfig?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _context.aiModelConfigs.FindAsync([id], ct);

    public async Task<AiModelConfig> CreateAsync(string name, string apiUrl, string plainApiKey, string modelId,
        AiModelProvider provider, string? proxyUrl = null, CancellationToken ct = default)
    {
        UrlSafety.EnsureSafeHttpUrl(apiUrl, allowPrivateHosts: true);
        if (!UrlSafety.TryValidateProxyUrl(proxyUrl, out var proxyError, allowPrivateHosts: true))
            throw new ArgumentException(proxyError);

        var model = new AiModelConfig
        {
            name = name,
            apiUrl = apiUrl,
            apiKeyEncrypted = _encryption.Encrypt(plainApiKey),
            modelId = modelId,
            provider = provider,
            proxyUrl = string.IsNullOrWhiteSpace(proxyUrl) ? null : proxyUrl.Trim(),
            isDefault = !await _context.aiModelConfigs.AnyAsync(ct)
        };
        _context.aiModelConfigs.Add(model);
        await _context.SaveChangesAsync(ct);
        return model;
    }

    public async Task UpdateAsync(AiModelConfig model, string? name, string? apiUrl, string? plainApiKey,
        string? modelId, AiModelProvider? provider, string? proxyUrl = null, CancellationToken ct = default)
    {
        if (apiUrl != null)
        {
            UrlSafety.EnsureSafeHttpUrl(apiUrl, allowPrivateHosts: true);
            model.apiUrl = apiUrl;
        }
        if (name != null) model.name = name;
        if (plainApiKey != null) model.apiKeyEncrypted = _encryption.Encrypt(plainApiKey);
        if (modelId != null) model.modelId = modelId;
        if (provider.HasValue) model.provider = provider.Value;
        if (proxyUrl != null)
        {
            if (!UrlSafety.TryValidateProxyUrl(proxyUrl, out var proxyError, allowPrivateHosts: true))
                throw new ArgumentException(proxyError);
            model.proxyUrl = string.IsNullOrWhiteSpace(proxyUrl) ? null : proxyUrl.Trim();
        }
        await _context.SaveChangesAsync(ct);
    }

    public async Task SetDefaultAsync(int id, CancellationToken ct = default)
    {
        var models = await _context.aiModelConfigs.ToListAsync(ct);
        foreach (var model in models)
            model.isDefault = model.Id == id;
        await _context.SaveChangesAsync(ct);
    }

    public async Task ToggleEnabledAsync(int id, CancellationToken ct = default)
    {
        var model = await _context.aiModelConfigs.FindAsync([id], ct)
            ?? throw new InvalidOperationException("AI model not found");
        model.isEnabled = !model.isEnabled;
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(AiModelConfig model, CancellationToken ct = default)
    {
        _context.aiModelConfigs.Remove(model);
        await _context.SaveChangesAsync(ct);

        // Make first remaining model the default if deleted one was default
        if (model.isDefault)
        {
            var first = await _context.aiModelConfigs.FirstOrDefaultAsync(ct);
            if (first != null)
            {
                first.isDefault = true;
                await _context.SaveChangesAsync(ct);
            }
        }
    }

    public AiModelConfig DecryptForUse(AiModelConfig model)
    {
        // Return a copy with decrypted key for actual API calls.
        // Some older rows stored the API key in plaintext — accept those until migration re-encrypts.
        string plainKey;
        try
        {
            plainKey = _encryption.IsCiphertext(model.apiKeyEncrypted)
                ? _encryption.Decrypt(model.apiKeyEncrypted)
                : model.apiKeyEncrypted ?? "";
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            throw ApiException.BadRequest(
                "AI model API key could not be decrypted. Re-enter the key in Settings → AI models.",
                code: "ai_api_key_invalid");
        }

        return new AiModelConfig
        {
            Id = model.Id,
            name = model.name,
            apiUrl = model.apiUrl,
            apiKeyEncrypted = plainKey,
            modelId = model.modelId,
            provider = model.provider,
            maxTokens = model.maxTokens,
            isDefault = model.isDefault,
            isEnabled = model.isEnabled,
            proxyUrl = model.proxyUrl,
        };
    }

    public async Task<(bool Success, string? Error)> TestConnectionAsync(AiModelConfig model, CancellationToken ct = default)
    {
        var decrypted = DecryptForUse(model);
        var request = new AiRequest
        {
            model = decrypted.modelId,
            max_tokens = 10,
            messages = [new AiMessage { role = "user", content = "Hi" }]
        };
        try
        {
            var response = await _apiClient.SendAsync(decrypted, request, ct);
            // Any response without exception means the model is reachable.
            // Some models return no content for trivial prompts while still being fully functional.
            _ = response;
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Fetches available model IDs from the provider's /v1/models endpoint.
    /// OpenAI-compatible servers (LM Studio, Ollama, vLLM, api.openai.com) go through
    /// the official OpenAI SDK; Anthropic uses a direct GET (trivial endpoint).
    /// </summary>
    public async Task<List<string>> DiscoverModelsAsync(
        string apiUrl, string? apiKey, AiModelProvider provider, string? proxyUrl = null, CancellationToken ct = default)
    {
        UrlSafety.EnsureSafeHttpUrl(apiUrl, allowPrivateHosts: true);
        if (!UrlSafety.TryValidateProxyUrl(proxyUrl, out var proxyError, allowPrivateHosts: true))
            throw new ArgumentException(proxyError);

        // Normalize: strip known completion paths
        var baseUrl = apiUrl.TrimEnd('/');
        foreach (var suffix in new[] { "/chat/completions", "/v1/messages", "/messages", "/v1" })
        {
            if (baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        if (provider == AiModelProvider.OpenAiCompatible)
        {
            var key = string.IsNullOrWhiteSpace(apiKey) ? "not-needed" : apiKey;
            var options = new OpenAI.OpenAIClientOptions { Endpoint = new Uri($"{baseUrl}/v1") };
            var proxied = AiApiClient.CreateProxiedClient(proxyUrl);
            if (proxied != null)
                options.Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(proxied);
            var modelClient = new OpenAI.Models.OpenAIModelClient(
                new System.ClientModel.ApiKeyCredential(key), options);
            var models = await modelClient.GetModelsAsync(ct);
            return models.Value.Select(m => m.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
        }

        // Anthropic: plain GET /v1/models
        using var http = string.IsNullOrWhiteSpace(proxyUrl)
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(15) }
            : AiApiClient.CreateProxiedClient(proxyUrl)!;
        if (http.Timeout == TimeSpan.Zero || http.Timeout == Timeout.InfiniteTimeSpan)
            http.Timeout = TimeSpan.FromSeconds(15);

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/models");
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
        }

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var ids = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var m in data.EnumerateArray())
                if (m.TryGetProperty("id", out var id) && id.GetString() is string s && s.Length > 0)
                    ids.Add(s);
        }
        return ids;
    }
}
