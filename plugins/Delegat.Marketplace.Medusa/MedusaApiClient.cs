using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.Medusa.Models;

namespace Delegat.Marketplace.Medusa;

public sealed class MedusaApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly ConnectionContext? _context;
    private readonly bool _ownsHttp;
    private bool _disposed;

    public MedusaApiClient(
        string apiBaseUrl,
        string apiKey,
        HttpClient? httpClient = null,
        ConnectionContext? context = null)
    {
        _baseUrl = apiBaseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _context = context;
        _ownsHttp = httpClient == null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<MedusaHealthResponse> GetHealthAsync(CancellationToken ct = default)
        => GetAsync<MedusaHealthResponse>("health", ct);

    public Task<MedusaCategoriesResponse> GetCategoriesAsync(
        string? parentId = null,
        string? search = null,
        CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(parentId))
            qs.Add($"parent_id={Uri.EscapeDataString(parentId)}");
        if (!string.IsNullOrWhiteSpace(search))
            qs.Add($"search={Uri.EscapeDataString(search)}");
        var path = qs.Count == 0 ? "categories" : $"categories?{string.Join("&", qs)}";
        return GetAsync<MedusaCategoriesResponse>(path, ct);
    }

    public Task<MedusaFieldsResponse> GetCategoryFieldsAsync(string categoryId, CancellationToken ct = default)
        => GetAsync<MedusaFieldsResponse>($"categories/{Uri.EscapeDataString(categoryId)}/fields", ct);

    public Task<MedusaUpsertCategoryResponse> UpsertCategoryAsync(
        MedusaUpsertCategoryRequest request,
        CancellationToken ct = default)
        => PostAsync<MedusaUpsertCategoryRequest, MedusaUpsertCategoryResponse>("categories/upsert", request, ct);

    public Task<MedusaUpsertProductResponse> UpsertProductAsync(
        MedusaProductPayload request,
        CancellationToken ct = default)
        => PostAsync<MedusaProductPayload, MedusaUpsertProductResponse>("products/upsert", request, ct);

    public Task<MedusaBulkProductResponse> UpsertProductsBulkAsync(
        MedusaBulkProductRequest request,
        CancellationToken ct = default)
        => PostAsync<MedusaBulkProductRequest, MedusaBulkProductResponse>("products/bulk", request, ct);

    public Task<MedusaBulkMutationResponse> ExportPricesAsync(
        MedusaBulkPricesRequest request,
        CancellationToken ct = default)
        => PostAsync<MedusaBulkPricesRequest, MedusaBulkMutationResponse>("prices/bulk", request, ct);

    public Task<MedusaBulkMutationResponse> ExportStocksAsync(
        MedusaBulkStocksRequest request,
        CancellationToken ct = default)
        => PostAsync<MedusaBulkStocksRequest, MedusaBulkMutationResponse>("stocks/bulk", request, ct);

    public Task<MedusaUpsertGroupResponse> UpsertGroupAsync(
        MedusaUpsertGroupRequest request,
        CancellationToken ct = default)
        => PostAsync<MedusaUpsertGroupRequest, MedusaUpsertGroupResponse>("groups/upsert", request, ct);

    public Task<MedusaGroupsResponse> GetGroupsAsync(CancellationToken ct = default)
        => GetAsync<MedusaGroupsResponse>("groups", ct);

    public Task<MedusaGroupMembershipResponse> AddProductsToGroupAsync(
        string collectionId,
        MedusaGroupMembershipRequest request,
        CancellationToken ct = default)
        => PostAsync<MedusaGroupMembershipRequest, MedusaGroupMembershipResponse>(
            $"groups/{Uri.EscapeDataString(collectionId)}/products",
            request,
            ct);

    public Task<MedusaGroupMembershipResponse> RemoveProductsFromGroupAsync(
        string collectionId,
        MedusaGroupMembershipRequest request,
        CancellationToken ct = default)
        => PostAsync<MedusaGroupMembershipRequest, MedusaGroupMembershipResponse>(
            $"groups/{Uri.EscapeDataString(collectionId)}/products/remove",
            request,
            ct);

    public Task<MedusaDeleteGroupResponse> DeleteGroupAsync(
        string collectionId,
        CancellationToken ct = default)
        => PostAsync<object, MedusaDeleteGroupResponse>(
            $"groups/{Uri.EscapeDataString(collectionId)}/delete",
            new { },
            ct);

    public Task<MedusaOrderListResponse> ListOrdersAsync(
        int page,
        int pageSize,
        string? status,
        string? search,
        CancellationToken ct = default)
    {
        var qs = $"page={page}&page_size={pageSize}";
        if (!string.IsNullOrWhiteSpace(status))
            qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(search))
            qs += $"&search={Uri.EscapeDataString(search)}";
        return GetAsync<MedusaOrderListResponse>($"orders?{qs}", ct);
    }

    public Task<MedusaOrderGetResponse> GetOrderAsync(string orderId, CancellationToken ct = default)
        => GetAsync<MedusaOrderGetResponse>($"orders/{Uri.EscapeDataString(orderId)}", ct);

    public Task<MedusaOrderGetResponse> CancelOrderAsync(
        string orderId,
        bool refund,
        CancellationToken ct = default)
        => PostAsync<object, MedusaOrderGetResponse>(
            $"orders/{Uri.EscapeDataString(orderId)}/cancel",
            new { refund },
            ct);

    public Task<MedusaOrderGetResponse> UpdateOrderStatusAsync(
        string orderId,
        string action,
        CancellationToken ct = default)
        => PostAsync<object, MedusaOrderGetResponse>(
            $"orders/{Uri.EscapeDataString(orderId)}/status",
            new { action },
            ct);

    public Task<MedusaOrderLabelResponse> GetOrderLabelAsync(
        string orderId,
        CancellationToken ct = default)
        => GetAsync<MedusaOrderLabelResponse>(
            $"orders/{Uri.EscapeDataString(orderId)}/label",
            ct);

    public async Task<MedusaProductGetResponse?> GetProductAsync(string productId, CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<MedusaProductGetResponse>(
                $"products/{Uri.EscapeDataString(productId)}",
                ct);
        }
        catch (MedusaApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private Task<T> GetAsync<T>(string relativePath, CancellationToken ct)
        => SendAsync<T>(HttpMethod.Get, relativePath, requestBody: null, ct);

    private Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest body,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return SendAsync<TResponse>(HttpMethod.Post, relativePath, json, ct);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        string? requestBody,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(relativePath));
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(_apiKey + ":"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        if (requestBody != null)
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        string? responseBody = null;
        Exception? error = null;

        try
        {
            response = await _http.SendAsync(request, ct);
            responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new MedusaApiException(
                    $"Medusa bridge HTTP {(int)response.StatusCode}: {Truncate(responseBody)}",
                    (int)response.StatusCode,
                    responseBody);
            }

            if (string.IsNullOrWhiteSpace(responseBody))
                throw new MedusaApiException("Empty response from Medusa bridge", (int)response.StatusCode);

            try
            {
                var parsed = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
                if (parsed is null)
                    throw new MedusaApiException("Failed to deserialize bridge response", (int)response.StatusCode, responseBody);
                return parsed;
            }
            catch (JsonException ex)
            {
                throw new MedusaApiException(
                    $"Invalid JSON from Medusa bridge: {ex.Message}",
                    (int)response.StatusCode,
                    responseBody);
            }
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            await AdapterHttpLogHelper.LogExchangeAsync(
                _context?.HttpLogger,
                _context,
                request,
                response,
                requestBody,
                responseBody,
                sw.ElapsedMilliseconds,
                error);
            response?.Dispose();
        }
    }

    private string BuildUrl(string relativePath)
    {
        var path = relativePath.TrimStart('/');
        return $"{_baseUrl}/{path}";
    }

    private static string Truncate(string? value, int max = 500)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value[..max] + "…";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttp)
            _http.Dispose();
    }
}
