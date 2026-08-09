using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Configuration;

namespace Delegat.Services.PlatformLink;

public interface IPlatformLinkClient
{
    bool IsEnabled { get; }
    string? InstanceId { get; }
    Task<IReadOnlyList<PlatformCurrencyDto>> GetCurrenciesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PlatformTimezoneDto>> GetTimezonesAsync(CancellationToken ct = default);
    Task ReportErrorAsync(PlatformErrorReportDto report, CancellationToken ct = default);
    Task SendHeartbeatAsync(PlatformHeartbeatDto heartbeat, CancellationToken ct = default);
    Task<PlatformEntitlementsDto?> GetEntitlementsAsync(CancellationToken ct = default);
}

public sealed class PlatformLinkClient : IPlatformLinkClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly PlatformLinkOptions _options;
    private readonly ILogger<PlatformLinkClient> _logger;

    public PlatformLinkClient(
        HttpClient http,
        PlatformLinkOptions options,
        ILogger<PlatformLinkClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public bool IsEnabled => _options.IsEnabled;
    public string? InstanceId => _options.InstanceId;

    public Task<IReadOnlyList<PlatformCurrencyDto>> GetCurrenciesAsync(CancellationToken ct = default) =>
        GetCatalogAsync<PlatformCurrencyDto>("/platform/v1/catalog/currencies", ct);

    public Task<IReadOnlyList<PlatformTimezoneDto>> GetTimezonesAsync(CancellationToken ct = default) =>
        GetCatalogAsync<PlatformTimezoneDto>("/platform/v1/catalog/timezones", ct);

    public async Task ReportErrorAsync(PlatformErrorReportDto report, CancellationToken ct = default)
    {
        if (!IsEnabled) return;
        await SendAsync(HttpMethod.Post, "/platform/v1/errors", report, ct);
    }

    public async Task SendHeartbeatAsync(PlatformHeartbeatDto heartbeat, CancellationToken ct = default)
    {
        if (!IsEnabled) return;
        await SendAsync(HttpMethod.Post, "/platform/v1/heartbeat", heartbeat, ct);
    }

    public async Task<PlatformEntitlementsDto?> GetEntitlementsAsync(CancellationToken ct = default)
    {
        if (!IsEnabled) return null;

        using var response = await SendRawAsync(HttpMethod.Get, "/platform/v1/entitlements", body: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Platform Link GET /platform/v1/entitlements returned {StatusCode}",
                (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<PlatformEntitlementsDto>(stream, JsonOptions, ct);
    }

    private async Task<IReadOnlyList<T>> GetCatalogAsync<T>(string path, CancellationToken ct)
    {
        if (!IsEnabled)
            return Array.Empty<T>();

        using var response = await SendRawAsync(HttpMethod.Get, path, body: null, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        JsonElement arrayEl;
        if (root.ValueKind == JsonValueKind.Array)
            arrayEl = root;
        else if (root.ValueKind == JsonValueKind.Object
                 && root.TryGetProperty("items", out var items)
                 && items.ValueKind == JsonValueKind.Array)
            arrayEl = items;
        else
            return Array.Empty<T>();

        var list = new List<T>();
        foreach (var el in arrayEl.EnumerateArray())
        {
            var item = el.Deserialize<T>(JsonOptions);
            if (item != null)
                list.Add(item);
        }

        return list;
    }

    private async Task SendAsync<T>(HttpMethod method, string path, T body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var response = await SendRawAsync(method, path, json, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Platform Link {Method} {Path} returned {StatusCode}",
                method, path, (int)response.StatusCode);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        string? body,
        CancellationToken ct)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Platform Link is not configured");

        var attempts = method == HttpMethod.Get ? 2 : 1;
        HttpResponseMessage? lastResponse = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            lastResponse?.Dispose();
            lastResponse = null;

            try
            {
                using var request = BuildSignedRequest(method, path, body);
                lastResponse = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)lastResponse.StatusCode < 500 || attempt == attempts - 1)
                    return lastResponse;

                _logger.LogWarning(
                    "Platform Link GET {Path} returned {StatusCode}, retrying",
                    path, (int)lastResponse.StatusCode);
            }
            catch (Exception ex) when (attempt < attempts - 1 && ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Platform Link GET {Path} failed, retrying", path);
            }
        }

        return lastResponse ?? throw new HttpRequestException($"Platform Link request failed: {method} {path}");
    }

    private HttpRequestMessage BuildSignedRequest(HttpMethod method, string path, string? body)
    {
        var bodyBytes = body == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body);
        var bodySha = PlatformLinkHmac.Sha256Hex(bodyBytes);
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var canonical = PlatformLinkHmac.BuildCanonical(method.Method, path, unix, bodySha);
        var signature = PlatformLinkHmac.Sign(_options.InstanceLinkSecret!, canonical);

        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(PlatformLinkHmac.HeaderInstanceId, _options.InstanceId);
        request.Headers.TryAddWithoutValidation(PlatformLinkHmac.HeaderTimestamp, unix.ToString());
        request.Headers.TryAddWithoutValidation(PlatformLinkHmac.HeaderSignature, signature);

        if (body != null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        return request;
    }
}
