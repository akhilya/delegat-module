using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Security;
using Delegat.Models.Ai;

namespace Delegat.Services.Ai;

public sealed class AiMcpServerService
{
    public const int MaxServers = 20;

    private readonly DataContext _db;
    private readonly EncryptionService _encryption;

    public AiMcpServerService(DataContext db, EncryptionService encryption)
    {
        _db = db;
        _encryption = encryption;
    }

    public Task<List<AiMcpServer>> GetAllAsync(CancellationToken ct = default)
        => _db.aiMcpServers.AsNoTracking().OrderBy(s => s.createdAt).ToListAsync(ct);

    public Task<List<AiMcpServer>> GetEnabledAsync(CancellationToken ct = default)
        => _db.aiMcpServers.AsNoTracking()
            .Where(s => s.isEnabled)
            .OrderBy(s => s.createdAt)
            .Take(MaxServers)
            .ToListAsync(ct);

    public Task<AiMcpServer?> GetByIdAsync(int id, CancellationToken ct = default)
        => _db.aiMcpServers.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<AiMcpServer> CreateAsync(string name, string url, string? plainApiKey, CancellationToken ct = default)
    {
        UrlSafety.EnsureSafeHttpUrl(url, allowPrivateHosts: true);
        if (await _db.aiMcpServers.CountAsync(ct) >= MaxServers)
            throw new InvalidOperationException($"At most {MaxServers} MCP servers are allowed");

        var row = new AiMcpServer
        {
            name = name.Trim(),
            url = url.Trim(),
            apiKeyEncrypted = string.IsNullOrWhiteSpace(plainApiKey) ? null : _encryption.Encrypt(plainApiKey.Trim()),
            isEnabled = true,
            createdAt = DateTime.UtcNow,
        };
        _db.aiMcpServers.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public async Task UpdateAsync(AiMcpServer row, string? name, string? url, string? plainApiKey, CancellationToken ct = default)
    {
        if (url != null)
        {
            UrlSafety.EnsureSafeHttpUrl(url, allowPrivateHosts: true);
            row.url = url.Trim();
        }
        if (name != null) row.name = name.Trim();
        if (!string.IsNullOrWhiteSpace(plainApiKey))
            row.apiKeyEncrypted = _encryption.Encrypt(plainApiKey.Trim());
        await _db.SaveChangesAsync(ct);
    }

    public async Task ToggleEnabledAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.aiMcpServers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException("MCP server not found");
        row.isEnabled = !row.isEnabled;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(AiMcpServer row, CancellationToken ct = default)
    {
        _db.aiMcpServers.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public string? DecryptApiKey(AiMcpServer row)
    {
        if (string.IsNullOrEmpty(row.apiKeyEncrypted)) return null;
        if (!_encryption.IsCiphertext(row.apiKeyEncrypted)) return row.apiKeyEncrypted;
        return _encryption.Decrypt(row.apiKeyEncrypted);
    }
}
