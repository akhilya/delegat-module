using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK;
using Delegat.Models.Ai;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Read-only Marketplace broker. It keeps credentials server-side and returns only
/// capped/redacted response data to the sandbox caller.
/// </summary>
public sealed class MarketplaceCredentialBroker
{
    public const int MaxResponseBytes = 256_000;
    private const int MaxPathLength = 2_048;
    private const int MaxOperationArgumentsLength = 16_384;

    private readonly DataContext _db;
    private readonly EncryptionService _encryption;
    private readonly HttpClient _http;
    private readonly ILogger<MarketplaceCredentialBroker> _logger;
    private readonly IMarketplaceAdapterRegistry? _adapters;

    public MarketplaceCredentialBroker(
        DataContext db,
        EncryptionService encryption,
        HttpClient http,
        ILogger<MarketplaceCredentialBroker> logger,
        IMarketplaceAdapterRegistry? adapters = null)
    {
        _db = db;
        _encryption = encryption;
        _http = http;
        _logger = logger;
        _adapters = adapters;
    }

    public async Task<MarketplaceBrokerReadResult> ReadAsync(
        int storeId,
        int userId,
        int connectionId,
        string? hostKey,
        string? path,
        CancellationToken ct)
    {
        if (!IsSafePath(path))
            return MarketplaceBrokerReadResult.Fail("invalid_path");

        var connection = await _db.marketplaceConnections.AsNoTracking()
            .Where(c => c.Id == connectionId && c.storeId == storeId)
            .FirstOrDefaultAsync(ct);
        if (connection == null)
            return MarketplaceBrokerReadResult.Fail("connection_not_found");
        if (!connection.isActive)
            return MarketplaceBrokerReadResult.Fail("connection_inactive");

        var adapter = (connection.adapterKey ?? "").Trim().ToLowerInvariant();
        var credentialsJson = ConnectionCredentialsHelper.DecryptCredentialsJson(
            connection.credentialsJson, _encryption);
        var credentials = ConnectionCredentialsHelper.ParseCredentialsObject(credentialsJson);

        if (_adapters?.GetByKey(adapter) is IMarketplaceSandboxReadBrokerAdapter pluginBroker)
        {
            try
            {
                var pluginResult = await pluginBroker.ReadAsync(connection, hostKey!, path!, ct);
                if (pluginResult != null)
                {
                    if (!string.IsNullOrEmpty(pluginResult.Error))
                        return MarketplaceBrokerReadResult.Fail(IsSafeBrokerError(pluginResult.Error)
                            ? pluginResult.Error
                            : "upstream_unavailable");
                    if (pluginResult.StatusCode is not { } pluginStatus || pluginResult.Body == null)
                        return MarketplaceBrokerReadResult.Fail("upstream_unavailable");

                    var pluginBody = CapAndRedact(pluginResult.Body, credentials, out var exceededCap);
                    var result = MarketplaceBrokerReadResult.Success(
                        pluginStatus,
                        pluginBody,
                        pluginResult.Truncated || exceededCap);
                    await AuditAsync(storeId, userId, connectionId, adapter, hostKey!, pluginStatus, ct);
                    return result;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return MarketplaceBrokerReadResult.Fail("upstream_timeout");
            }
            catch (HttpRequestException)
            {
                return MarketplaceBrokerReadResult.Fail("upstream_unavailable");
            }
        }

        if (adapter is not ("wildberries" or "ozon" or "yandex" or "yandexmarket"))
            return MarketplaceBrokerReadResult.Fail("adapter_not_supported");

        var hosts = SandboxShops.Hosts(adapter, credentials);
        if (string.IsNullOrWhiteSpace(hostKey)
            || !hosts.TryGetValue(hostKey, out var host)
            || !Uri.TryCreate(host, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUri.UserInfo))
            return MarketplaceBrokerReadResult.Fail("host_not_allowed");

        if (!TryCreateTargetUri(baseUri, path!, out var targetUri))
            return MarketplaceBrokerReadResult.Fail("invalid_path");

        using var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Delegat-Marketplace-Broker/1.0");

        var authError = AddAuthentication(request, adapter, credentials);
        if (authError != null)
            return MarketplaceBrokerReadResult.Fail(authError);

        try
        {
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            var (rawBody, upstreamTruncated) = await ReadCappedAsync(response.Content, MaxResponseBytes, ct);
            var body = RedactCredentialValues(rawBody, credentials);
            var status = (int)response.StatusCode;
            var result = MarketplaceBrokerReadResult.Success(status, body, upstreamTruncated);
            await AuditAsync(storeId, userId, connectionId, adapter, hostKey!, status, ct);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return MarketplaceBrokerReadResult.Fail("upstream_timeout");
        }
        catch (HttpRequestException)
        {
            return MarketplaceBrokerReadResult.Fail("upstream_unavailable");
        }
    }

    /// <summary>
    /// Dispatch an allow-listed, adapter-defined read operation. This route intentionally
    /// has no generic HTTP method/path/body forwarding and never falls back to fixed-host GET.
    /// </summary>
    public async Task<MarketplaceBrokerReadResult> ReadOperationAsync(
        int storeId,
        int userId,
        int connectionId,
        string? operation,
        JsonElement arguments,
        CancellationToken ct)
    {
        if (!IsSafeOperation(operation))
            return MarketplaceBrokerReadResult.Fail("invalid_operation");
        if (arguments.ValueKind != JsonValueKind.Object
            || arguments.GetRawText().Length > MaxOperationArgumentsLength)
            return MarketplaceBrokerReadResult.Fail("invalid_arguments");

        var connection = await _db.marketplaceConnections.AsNoTracking()
            .Where(c => c.Id == connectionId && c.storeId == storeId)
            .FirstOrDefaultAsync(ct);
        if (connection == null)
            return MarketplaceBrokerReadResult.Fail("connection_not_found");
        if (!connection.isActive)
            return MarketplaceBrokerReadResult.Fail("connection_inactive");

        var adapter = (connection.adapterKey ?? "").Trim().ToLowerInvariant();
        if (_adapters?.GetByKey(adapter) is not IMarketplaceSandboxReadBrokerAdapter pluginBroker)
            return MarketplaceBrokerReadResult.Fail("adapter_not_supported");

        MarketplaceSandboxReadResponse? pluginResult;
        try
        {
            pluginResult = await pluginBroker.ReadOperationAsync(connection, operation!, arguments, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return MarketplaceBrokerReadResult.Fail("upstream_timeout");
        }
        catch (HttpRequestException)
        {
            return MarketplaceBrokerReadResult.Fail("upstream_unavailable");
        }
        catch (IOException)
        {
            return MarketplaceBrokerReadResult.Fail("upstream_unavailable");
        }

        if (pluginResult == null)
            return MarketplaceBrokerReadResult.Fail("operation_not_supported");
        if (!string.IsNullOrEmpty(pluginResult.Error))
            return MarketplaceBrokerReadResult.Fail(IsSafeBrokerError(pluginResult.Error)
                ? pluginResult.Error
                : "upstream_unavailable");
        if (pluginResult.StatusCode is not (>= 100 and <= 599) || pluginResult.Body == null)
            return MarketplaceBrokerReadResult.Fail("upstream_unavailable");

        var credentialsJson = ConnectionCredentialsHelper.DecryptCredentialsJson(
            connection.credentialsJson, _encryption);
        var credentials = ConnectionCredentialsHelper.ParseCredentialsObject(credentialsJson);
        var body = CapAndRedact(pluginResult.Body, credentials, out var exceededCap);
        var result = MarketplaceBrokerReadResult.Success(
            pluginResult.StatusCode.Value,
            body,
            pluginResult.Truncated || exceededCap);
        await AuditOperationAsync(storeId, userId, connectionId, adapter, operation!, pluginResult.StatusCode.Value, ct);
        return result;
    }

    public async Task<MarketplaceBrokerReadOperationsResult> GetReadOperationsAsync(
        int storeId, int connectionId, CancellationToken ct)
    {
        var connection = await _db.marketplaceConnections.AsNoTracking()
            .Where(c => c.Id == connectionId && c.storeId == storeId)
            .Select(c => new { c.adapterKey, c.isActive })
            .FirstOrDefaultAsync(ct);
        if (connection == null)
            return MarketplaceBrokerReadOperationsResult.Fail("connection_not_found");
        if (!connection.isActive)
            return MarketplaceBrokerReadOperationsResult.Fail("connection_inactive");

        var adapterKey = (connection.adapterKey ?? "").Trim().ToLowerInvariant();
        var adapter = _adapters?.GetByKey(adapterKey);
        if (adapter is not IMarketplaceSandboxReadBrokerAdapter broker)
            return MarketplaceBrokerReadOperationsResult.Fail("adapter_not_supported");

        return MarketplaceBrokerReadOperationsResult.Success(broker.GetReadOperations()
            .Where(operation => IsSafeOperation(operation.Name)
                && operation.Description.Length <= 500
                && operation.ArgumentsSchemaJson.Length <= 8_192)
            .ToArray());
    }

    private static bool IsSafeBrokerError(string error) => error is
        "invalid_path" or "host_not_allowed" or "credentials_missing" or "adapter_not_supported"
        or "invalid_operation" or "invalid_arguments" or "operation_not_supported"
        or "upstream_timeout" or "upstream_unavailable";

    private static bool IsSafeOperation(string? operation) =>
        !string.IsNullOrWhiteSpace(operation)
        && operation.Length <= 64
        && operation[0] is >= 'a' and <= 'z'
        && operation.All(character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_');

    private string CapAndRedact(
        string body,
        IReadOnlyDictionary<string, string> credentials,
        out bool truncated)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        truncated = bytes.Length > MaxResponseBytes;
        if (truncated)
            body = Encoding.UTF8.GetString(bytes, 0, MaxResponseBytes);
        return RedactCredentialValues(body, credentials);
    }

    private async Task AuditAsync(
        int storeId, int userId, int connectionId, string adapter, string hostKey, int status, CancellationToken ct)
    {
        _db.aiAgentAudits.Add(new AiAgentAudit
        {
            userId = userId,
            storeId = storeId,
            tool = "marketplace_broker_read",
            summary = $"GET {adapter} connection {connectionId} host {hostKey}: HTTP {status}",
            createdAt = DateTime.UtcNow,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Marketplace broker audit could not be persisted for connection {ConnectionId}", connectionId);
        }
    }

    private async Task AuditOperationAsync(
        int storeId, int userId, int connectionId, string adapter, string operation, int status, CancellationToken ct)
    {
        _db.aiAgentAudits.Add(new AiAgentAudit
        {
            userId = userId,
            storeId = storeId,
            tool = "marketplace_broker_read",
            summary = $"READ {adapter} connection {connectionId} operation {operation}: HTTP {status}",
            createdAt = DateTime.UtcNow,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Marketplace broker audit could not be persisted for connection {ConnectionId}", connectionId);
        }
    }

    private static string? AddAuthentication(
        HttpRequestMessage request,
        string adapter,
        IReadOnlyDictionary<string, string> credentials)
    {
        switch (adapter)
        {
            case "wildberries":
                if (!TryCredential(credentials, "apiKey", out var wbKey))
                    return "credentials_missing";
                request.Headers.TryAddWithoutValidation("Authorization", wbKey);
                return null;

            case "ozon":
                if (!TryCredential(credentials, "clientId", out var clientId)
                    || !TryCredential(credentials, "apiKey", out var ozonKey))
                    return "credentials_missing";
                request.Headers.TryAddWithoutValidation("Client-Id", clientId);
                request.Headers.TryAddWithoutValidation("Api-Key", ozonKey);
                return null;

            case "yandex":
            case "yandexmarket":
                if (!TryCredential(credentials, "apiKey", out var yandexKey))
                    return "credentials_missing";
                request.Headers.TryAddWithoutValidation("Api-Key", yandexKey);
                return null;

            default:
                return "adapter_not_supported";
        }
    }

    private static bool TryCredential(
        IReadOnlyDictionary<string, string> credentials, string key, out string value)
    {
        value = "";
        if (!credentials.TryGetValue(key, out var candidate)
            || string.IsNullOrWhiteSpace(candidate)
            || candidate.Any(c => c < 0x21 || c > 0x7e))
            return false;
        value = candidate.Trim();
        return value.Length > 0;
    }

    private static bool IsSafePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Length <= MaxPathLength
        && path[0] == '/'
        && !path.StartsWith("//", StringComparison.Ordinal)
        && !path.Contains('\\')
        && !path.Contains('#')
        && !path.Any(char.IsControl);

    private static bool TryCreateTargetUri(Uri baseUri, string path, out Uri targetUri)
    {
        targetUri = null!;
        if (!Uri.TryCreate(path, UriKind.Relative, out var relative))
            return false;

        var target = new Uri(baseUri, relative);
        if (target.Scheme != Uri.UriSchemeHttps
            || !string.Equals(target.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || target.Port != baseUri.Port
            || !string.IsNullOrEmpty(target.UserInfo)
            || !string.IsNullOrEmpty(target.Fragment))
            return false;

        targetUri = target;
        return true;
    }

    private static string RedactCredentialValues(
        string body, IReadOnlyDictionary<string, string> credentials)
    {
        var sensitiveValues = credentials
            .Where(pair => IsSensitiveCredentialName(pair.Key) && !string.IsNullOrEmpty(pair.Value))
            .Select(pair => pair.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length);
        foreach (var value in sensitiveValues)
            body = body.Replace(value, "[redacted]", StringComparison.Ordinal);
        return body;
    }

    private static bool IsSensitiveCredentialName(string key) =>
        key.Contains("key", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("clientId", StringComparison.OrdinalIgnoreCase);

    private static async Task<(string Body, bool Truncated)> ReadCappedAsync(
        HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(Math.Min(maxBytes, 16_384));
        var chunk = new byte[8_192];
        var remaining = maxBytes + 1;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), ct);
            if (read == 0) break;
            var keep = Math.Min(read, maxBytes - (int)buffer.Length);
            if (keep > 0) buffer.Write(chunk, 0, keep);
            remaining -= read;
        }

        var bytes = buffer.ToArray();
        return (Encoding.UTF8.GetString(bytes), remaining == 0);
    }
}

public sealed record MarketplaceBrokerReadResult(
    bool Ok,
    int? Status,
    string? Body,
    bool Truncated,
    string? Error)
{
    public static MarketplaceBrokerReadResult Success(int status, string body, bool truncated) =>
        new(true, status, body, truncated, null);

    public static MarketplaceBrokerReadResult Fail(string error) =>
        new(false, null, null, false, error);
}

public sealed record MarketplaceBrokerReadOperationsResult(
    bool Ok,
    IReadOnlyList<MarketplaceSandboxReadOperationDescriptor>? Operations,
    string? Error)
{
    public static MarketplaceBrokerReadOperationsResult Success(
        IReadOnlyList<MarketplaceSandboxReadOperationDescriptor> operations) => new(true, operations, null);

    public static MarketplaceBrokerReadOperationsResult Fail(string error) => new(false, null, error);
}
