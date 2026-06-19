using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PrintO.Marketplace.OpenCart.Models;

namespace PrintO.Marketplace.OpenCart;

public sealed class OpenCartApiClient
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _apiBaseUrl;
    private readonly string _apiKey;
    private readonly ILogger? _logger;
    private readonly bool _queryActionMode;

    public OpenCartApiClient(string apiBaseUrl, string apiKey, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            throw new ArgumentException("OpenCart API base URL is required", nameof(apiBaseUrl));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("OpenCart API key is required", nameof(apiKey));

        _apiBaseUrl = apiBaseUrl.Trim();
        _apiKey = apiKey.Trim();
        _logger = logger;
        _queryActionMode = _apiBaseUrl.Contains('?', StringComparison.Ordinal);
    }

    public Task<OpenCartInfoResponse> GetInfoAsync(CancellationToken ct = default)
        => GetAsync<OpenCartInfoResponse>("health", null, ct);

    public Task<List<OpenCartCategoryDto>> GetCategoriesAsync(
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["parent_id"] = parentId,
            ["search"] = searchQuery
        };

        return GetAsync<List<OpenCartCategoryDto>>("categories", query, ct);
    }

    public Task<List<OpenCartFieldDto>> GetCategoryFieldsAsync(
        string categoryId,
        CancellationToken ct = default)
    {
        var action = _queryActionMode
            ? "category_fields"
            : $"categories/{Uri.EscapeDataString(categoryId)}/fields";

        var query = _queryActionMode
            ? new Dictionary<string, string?> { ["category_id"] = categoryId }
            : null;

        return GetAsync<List<OpenCartFieldDto>>(action, query, ct);
    }

    public Task<OpenCartProductsResponse> GetProductsAsync(
        int pageSize,
        string? cursor = null,
        string? offerIdSearch = null,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["page_size"] = pageSize.ToString(),
            ["cursor"] = cursor,
            ["offer_id_search"] = offerIdSearch
        };

        return GetAsync<OpenCartProductsResponse>("products", query, ct);
    }

    public Task<OpenCartCreateCategoryResponse> CreateCategoryAsync(
        OpenCartCreateCategoryRequest request,
        CancellationToken ct = default)
        => PostAsync<OpenCartCreateCategoryRequest, OpenCartCreateCategoryResponse>("categories/upsert", request, ct);

    public Task<OpenCartProductResponse> UpsertProductAsync(
        OpenCartProductPayload product,
        CancellationToken ct = default)
        => PostAsync<OpenCartProductPayload, OpenCartProductResponse>("products/upsert", product, ct);

    public Task<OpenCartBulkProductResponse> UpsertProductsBulkAsync(
        OpenCartBulkProductRequest request,
        CancellationToken ct = default)
        => PostAsync<OpenCartBulkProductRequest, OpenCartBulkProductResponse>("products/bulk", request, ct);

    public Task<OpenCartBulkOperationResponse> ExportPricesAsync(
        OpenCartBulkOperationRequest<OpenCartPricePayload> request,
        CancellationToken ct = default)
        => PostAsync<OpenCartBulkOperationRequest<OpenCartPricePayload>, OpenCartBulkOperationResponse>("prices/bulk", request, ct);

    public Task<OpenCartBulkOperationResponse> ExportStocksAsync(
        OpenCartBulkOperationRequest<OpenCartStockPayload> request,
        CancellationToken ct = default)
        => PostAsync<OpenCartBulkOperationRequest<OpenCartStockPayload>, OpenCartBulkOperationResponse>("stocks/bulk", request, ct);

    private async Task<TResponse> GetAsync<TResponse>(
        string action,
        Dictionary<string, string?>? query,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(action, query));
        PrepareRequest(request);
        return await SendAsync<TResponse>(request, ct);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string action,
        TRequest payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(action, null));
        PrepareRequest(request);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        return await SendAsync<TResponse>(request, ct);
    }

    private void PrepareRequest(HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-PrintO-Api-Key", _apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    private async Task<TResponse> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken ct)
    {
        _logger?.LogDebug("OpenCart request: {Method} {Uri}", request.Method, request.RequestUri);

        HttpResponseMessage response;
        try
        {
            response = await SharedHttpClient.SendAsync(request, ct);
        }
        catch (OpenCartApiException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new OpenCartApiException(
                (int)HttpStatusCode.GatewayTimeout,
                $"OpenCart bridge did not respond within {SharedHttpClient.Timeout.TotalSeconds:0} seconds",
                null,
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new OpenCartApiException(
                0,
                $"OpenCart bridge network error: {ex.Message}",
                null,
                ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var message = ExtractErrorMessage(body) ?? response.ReasonPhrase ?? "OpenCart API error";
                throw new OpenCartApiException((int)response.StatusCode, message, body);
            }

            if (string.IsNullOrWhiteSpace(body))
                return Activator.CreateInstance<TResponse>();

            TResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<TResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                var preview = body.Length > 500 ? body[..500] : body;
                throw new OpenCartApiException(
                    (int)HttpStatusCode.OK,
                    $"OpenCart bridge returned a non-JSON response: {preview}",
                    body);
            }

            if (result == null)
                throw new OpenCartApiException((int)HttpStatusCode.OK, "OpenCart API returned an empty response", body);

            return result;
        }
    }

    private Uri BuildUri(string action, Dictionary<string, string?>? query)
    {
        var builder = new StringBuilder(_apiBaseUrl.TrimEnd('/'));

        if (_queryActionMode)
        {
            if (!_apiBaseUrl.EndsWith("?", StringComparison.Ordinal) &&
                !_apiBaseUrl.EndsWith("&", StringComparison.Ordinal))
            {
                builder.Append('&');
            }

            builder.Append("action=").Append(Uri.EscapeDataString(NormalizeAction(action)));
        }
        else
        {
            builder.Append('/').Append(action.TrimStart('/'));
        }

        if (query != null)
        {
            foreach (var (key, value) in query)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                builder.Append(builder.ToString().Contains('?', StringComparison.Ordinal) ? '&' : '?');
                builder.Append(Uri.EscapeDataString(key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(value));
            }
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static string NormalizeAction(string action)
        => action.Replace("/", ".", StringComparison.Ordinal).Replace("_", ".", StringComparison.Ordinal);

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            foreach (var key in new[] { "error", "message", "error_message" })
            {
                if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
        }
        catch (JsonException)
        {
            return body.Length > 500 ? body[..500] : body;
        }

        return null;
    }
}
