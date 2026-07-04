using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;

namespace Delegat.Services.Ai;

public class AiModelConfigService
{
    private readonly DataContext _context;
    private readonly EncryptionService? _encryption;
    private readonly AiApiClient _apiClient;

    public AiModelConfigService(DataContext context, AiApiClient apiClient, EncryptionService? encryption = null)
    {
        _context = context;
        _encryption = encryption;
        _apiClient = apiClient;
    }

    public async Task<AiModelConfig?> GetDefaultModelAsync(CancellationToken ct = default)
        => await _context.aiModelConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.isDefault, ct)
            ?? await _context.aiModelConfigs.AsNoTracking().FirstOrDefaultAsync(ct);

    public async Task<List<AiModelConfig>> GetAllAsync(CancellationToken ct = default)
        => await _context.aiModelConfigs.AsNoTracking().OrderBy(m => m.createdAt).ToListAsync(ct);

    public async Task<AiModelConfig?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _context.aiModelConfigs.FindAsync([id], ct);

    public async Task<AiModelConfig> CreateAsync(string name, string apiUrl, string plainApiKey, string modelId,
        AiModelProvider provider, int maxTokens, CancellationToken ct = default)
    {
        var model = new AiModelConfig
        {
            name = name,
            apiUrl = apiUrl,
            apiKeyEncrypted = _encryption?.Encrypt(plainApiKey) ?? plainApiKey,
            modelId = modelId,
            provider = provider,
            maxTokens = maxTokens,
            isDefault = !await _context.aiModelConfigs.AnyAsync(ct)
        };
        _context.aiModelConfigs.Add(model);
        await _context.SaveChangesAsync(ct);
        return model;
    }

    public async Task UpdateAsync(AiModelConfig model, string? name, string? apiUrl, string? plainApiKey,
        string? modelId, AiModelProvider? provider, int? maxTokens, CancellationToken ct = default)
    {
        if (name != null) model.name = name;
        if (apiUrl != null) model.apiUrl = apiUrl;
        if (plainApiKey != null) model.apiKeyEncrypted = _encryption?.Encrypt(plainApiKey) ?? plainApiKey;
        if (modelId != null) model.modelId = modelId;
        if (provider.HasValue) model.provider = provider.Value;
        if (maxTokens.HasValue) model.maxTokens = maxTokens.Value;
        await _context.SaveChangesAsync(ct);
    }

    public async Task SetDefaultAsync(int id, CancellationToken ct = default)
    {
        await _context.aiModelConfigs
            .Where(m => m.isDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.isDefault, false), ct);
        await _context.aiModelConfigs
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.isDefault, true), ct);
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
        // Return a copy with decrypted key for actual API calls
        return new AiModelConfig
        {
            Id = model.Id,
            name = model.name,
            apiUrl = model.apiUrl,
            apiKeyEncrypted = _encryption?.Decrypt(model.apiKeyEncrypted) ?? model.apiKeyEncrypted,
            modelId = model.modelId,
            provider = model.provider,
            maxTokens = model.maxTokens,
            isDefault = model.isDefault
        };
    }

    public async Task<bool> TestConnectionAsync(AiModelConfig model, CancellationToken ct = default)
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
            return response.content.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
