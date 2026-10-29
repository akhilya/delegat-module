using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.SDK;

namespace Delegat.Marketplace.Avito;

internal sealed class AvitoApiClient : IDisposable
{
    private static readonly Uri ApiBase = new("https://api.avito.ru/");
    private readonly HttpClient _http;
    private readonly ConnectionContext _context;
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    internal AvitoApiClient(
        ConnectionContext context,
        HttpMessageHandler? handler = null,
        bool disableRedirects = false)
    {
        _context = context;
        _http = handler is not null
            ? new HttpClient(handler, disposeHandler: true)
            : disableRedirects
                ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
                : new HttpClient();
        _http.BaseAddress = ApiBase;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    internal async Task<AvitoAccount> GetCurrentAccountAsync(CancellationToken ct)
    {
        using var document = await SendJsonAsync(HttpMethod.Get, "core/v1/accounts/self", null, ct);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var id) || id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
            throw new InvalidDataException("Avito account response did not contain an account ID.");

        return new AvitoAccount(id.ToString(), GetString(root, "name"));
    }

    internal async Task<IReadOnlyList<AvitoItem>> GetItemsPageAsync(
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var path = $"core/v1/items?page={page}&per_page={pageSize}&status=active,old,blocked,rejected";
        using var document = await SendJsonAsync(HttpMethod.Get, path, null, ct);
        if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Avito items response did not contain a resources array.");

        return resources.EnumerateArray().Select(ParseItem).Where(x => x != null).Cast<AvitoItem>().ToList();
    }

    internal async Task<IReadOnlyList<AvitoCategoryNode>> GetCategoryTreeAsync(CancellationToken ct)
    {
        using var document = await SendJsonAsync(HttpMethod.Get, "autoload/v1/user-docs/tree", null, ct);
        if (!document.RootElement.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Avito category response did not contain a categories array.");

        return categories.EnumerateArray().Select(ParseCategory).Where(x => x != null).Cast<AvitoCategoryNode>().ToList();
    }

    internal async Task<JsonDocument> GetCategoryFieldsAsync(string categorySlug, CancellationToken ct)
    {
        var escapedSlug = Uri.EscapeDataString(categorySlug);
        return await SendJsonAsync(HttpMethod.Get, $"autoload/v1/user-docs/node/{escapedSlug}/fields", null, ct);
    }

    internal async Task<IReadOnlyList<AvitoFieldValue>> GetFieldValuesAsync(string valuesUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(valuesUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, ApiBase.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Avito supplied an unexpected field-values URL.");

        using var document = await SendJsonAsync(HttpMethod.Get, uri.PathAndQuery.TrimStart('/'), null, ct);
        var result = new List<AvitoFieldValue>();
        CollectFieldValues(document.RootElement, result);
        return result;
    }

    internal async Task<bool> UpdatePriceAsync(string itemId, int price, CancellationToken ct)
    {
        var escapedId = Uri.EscapeDataString(itemId);
        using var document = await SendJsonAsync(
            HttpMethod.Post,
            $"core/v1/items/{escapedId}/update_price",
            new Dictionary<string, int> { ["price"] = price },
            ct);

        return document.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.True;
    }

    internal async Task<MarketplaceSandboxReadResponse> ReadOnlyBrokerAsync(string path, CancellationToken ct)
    {
        const int maxBytes = 256_000;
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > 2_048
            || path[0] != '/'
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('\\')
            || path.Contains('#')
            || path.Any(char.IsControl)
            || !Uri.TryCreate(path, UriKind.Relative, out var relative))
            return new(null, null, false, "invalid_path");

        var target = new Uri(ApiBase, relative);
        if (target.Scheme != Uri.UriSchemeHttps
            || !string.Equals(target.Host, ApiBase.Host, StringComparison.OrdinalIgnoreCase)
            || target.Port != ApiBase.Port)
            return new(null, null, false, "host_not_allowed");

        await EnsureAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
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

        var body = Encoding.UTF8.GetString(buffer.ToArray());
        body = Redact(body, _accessToken);
        body = Redact(body, _context.GetCredential("clientId"));
        body = Redact(body, _context.GetCredential("clientSecret"));
        return new((int)response.StatusCode, body, remaining == 0);
    }

    private static string Redact(string value, string? secret) =>
        string.IsNullOrEmpty(secret) ? value : value.Replace(secret, "[redacted]", StringComparison.Ordinal);

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        await EnsureAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        string? requestBody = null;
        if (body != null)
        {
            requestBody = JsonSerializer.Serialize(body);
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        }

        var timer = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        string? responseBody = null;
        Exception? requestError = null;
        try
        {
            response = await _http.SendAsync(request, ct);
            responseBody = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new AvitoApiException(response.StatusCode, ReadError(responseBody));

            return JsonDocument.Parse(responseBody);
        }
        catch (Exception ex)
        {
            requestError = ex;
            throw;
        }
        finally
        {
            timer.Stop();
            await AdapterHttpLogHelper.LogExchangeAsync(
                _context.HttpLogger, _context, request, response, requestBody, responseBody,
                timer.ElapsedMilliseconds, requestError, ApiBase);
            response?.Dispose();
        }
    }

    private async Task EnsureAccessTokenAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt.AddSeconds(-30))
            return;

        var clientId = _context.GetCredential("clientId")?.Trim();
        var clientSecret = _context.GetCredential("clientSecret");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Enter the Avito API client ID and client secret.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            })
        };
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new AvitoApiException(response.StatusCode, ReadError(body));

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("access_token", out var token)
            || token.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(token.GetString()))
            throw new InvalidDataException("Avito token response did not contain an access token.");
        _accessToken = token.GetString();
        var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiry)
            && expiry.TryGetInt32(out var seconds) ? Math.Max(60, seconds) : 86400;
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
    }

    private static AvitoItem? ParseItem(JsonElement item)
    {
        var id = GetIdentifier(item, "id");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var category = item.TryGetProperty("category", out var categoryValue) ? categoryValue : default;
        return new AvitoItem(
            id,
            GetString(item, "title") ?? id,
            GetDecimal(item, "price"),
            GetIdentifier(category, "id"),
            GetString(category, "name"),
            GetString(item, "status"),
            GetString(item, "url"));
    }

    private static AvitoCategoryNode? ParseCategory(JsonElement item)
    {
        var name = GetString(item, "name");
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var children = new List<AvitoCategoryNode>();
        if (item.TryGetProperty("nested", out var nested))
            CollectCategories(nested, children);
        return new AvitoCategoryNode(GetString(item, "slug"), name, children);
    }

    private static void CollectCategories(JsonElement element, List<AvitoCategoryNode> result)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                CollectCategories(child, result);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("name", out _))
        {
            var parsed = ParseCategory(element);
            if (parsed != null)
                result.Add(parsed);
            return;
        }

        foreach (var property in element.EnumerateObject())
            CollectCategories(property.Value, result);
    }

    private static void CollectFieldValues(JsonElement element, List<AvitoFieldValue> result)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                CollectFieldValues(item, result);
            return;
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value)) result.Add(new AvitoFieldValue(value, value));
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return;

        var fieldValue = GetString(element, "value");
        if (!string.IsNullOrWhiteSpace(fieldValue))
        {
            result.Add(new AvitoFieldValue(fieldValue, GetString(element, "description") ?? fieldValue));
            return;
        }
        foreach (var property in element.EnumerateObject())
            CollectFieldValues(property.Value, result);
    }

    private static string? GetIdentifier(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.Number or JsonValueKind.String
            ? value.ToString()
            : null;

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? GetDecimal(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;

    private static string ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var nested)) root = nested;
            return GetString(root, "message")
                ?? GetString(root, "error_description")
                ?? GetString(root, "error")
                ?? "The marketplace returned an error.";
        }
        catch (JsonException)
        {
            return "The marketplace returned an unreadable error response.";
        }
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record AvitoAccount(string Id, string? Name);
internal sealed record AvitoItem(string Id, string Name, decimal? Price, string? CategoryId, string? CategoryName, string? Status, string? Url);
internal sealed record AvitoCategoryNode(string? Slug, string Name, IReadOnlyList<AvitoCategoryNode> Children);
internal sealed record AvitoFieldValue(string Value, string Label);

internal sealed class AvitoApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
