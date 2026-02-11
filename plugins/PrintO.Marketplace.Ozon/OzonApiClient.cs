using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PrintO.Marketplace.Ozon.Models;
using PrintO.Marketplace.SDK.RateLimit;

namespace PrintO.Marketplace.Ozon;

/// <summary>
/// HTTP client for Ozon Seller API.
/// Uses a shared static HttpClient and per-ClientId rate limiter.
/// </summary>
public class OzonApiClient
{
    private const string BaseUrl = "https://api-seller.ozon.ru";
    private const int MaxRetries = 3;

    private static readonly HttpClient SharedHttpClient = new()
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly ConcurrentDictionary<string, MarketplaceRateLimiter> RateLimiters = new();

    private readonly string _clientId;
    private readonly string _apiKey;
    private readonly MarketplaceRateLimiter _rateLimiter;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OzonApiClient(string clientId, string apiKey)
    {
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));

        _rateLimiter = RateLimiters.GetOrAdd(clientId,
            _ => new MarketplaceRateLimiter(maxRequestsPerSecond: 50));
    }

    /// <summary>
    /// Get the rate limiter for a specific Client ID.
    /// </summary>
    public static MarketplaceRateLimiter? GetRateLimiterForClientId(string clientId)
    {
        return RateLimiters.TryGetValue(clientId, out var limiter) ? limiter : null;
    }

    #region Category Methods

    /// <summary>
    /// Get category tree
    /// </summary>
    public async Task<CategoryTreeResponse> GetCategoryTreeAsync(CancellationToken ct = default)
    {
        var request = new CategoryTreeRequest { Language = "RU" };
        return await PostAsync<CategoryTreeRequest, CategoryTreeResponse>("/v1/description-category/tree", request, ct);
    }

    /// <summary>
    /// Get category attributes
    /// </summary>
    public async Task<CategoryAttributeResponse> GetCategoryAttributesAsync(
        long descriptionCategoryId,
        long typeId,
        CancellationToken ct = default)
    {
        var request = new CategoryAttributeRequest
        {
            DescriptionCategoryId = descriptionCategoryId,
            TypeId = typeId,
            Language = "RU"
        };
        return await PostAsync<CategoryAttributeRequest, CategoryAttributeResponse>("/v1/description-category/attribute", request, ct);
    }

    /// <summary>
    /// Get attribute dictionary values
    /// </summary>
    public async Task<AttributeValuesResponse> GetAttributeValuesAsync(
        long descriptionCategoryId,
        long typeId,
        long attributeId,
        long lastValueId = 0,
        long limit = 1000,
        CancellationToken ct = default)
    {
        var request = new AttributeValuesRequest
        {
            DescriptionCategoryId = descriptionCategoryId,
            TypeId = typeId,
            AttributeId = attributeId,
            Language = "RU",
            LastValueId = lastValueId,
            Limit = limit
        };
        return await PostAsync<AttributeValuesRequest, AttributeValuesResponse>("/v1/description-category/attribute/values", request, ct);
    }

    #endregion

    #region Product Methods

    /// <summary>
    /// Get product list (for pagination)
    /// </summary>
    public async Task<ProductListResponse> GetProductListAsync(
        string lastId = "",
        int limit = 100,
        List<string>? productIds = null,
        List<string>? offerIds = null,
        CancellationToken ct = default)
    {
        var request = new ProductListRequest
        {
            LastId = lastId,
            Limit = limit,
            Filter = new ProductListFilter
            {
                ProductId = productIds,
                OfferId = offerIds,
                Visibility = "ALL"
            }
        };
        return await PostAsync<ProductListRequest, ProductListResponse>("/v3/product/list", request, ct);
    }

    /// <summary>
    /// Get product info by IDs
    /// </summary>
    public async Task<ProductInfoListResponse> GetProductInfoAsync(
        List<string>? productIds = null,
        List<string>? offerIds = null,
        CancellationToken ct = default)
    {
        var request = new ProductInfoListRequest
        {
            ProductId = productIds,
            OfferId = offerIds
        };
        return await PostAsync<ProductInfoListRequest, ProductInfoListResponse>("/v3/product/info/list", request, ct);
    }

    /// <summary>
    /// Get product attributes
    /// </summary>
    public async Task<ProductAttributesResponse> GetProductAttributesAsync(
        string lastId = "",
        int limit = 100,
        List<string>? productIds = null,
        List<string>? offerIds = null,
        CancellationToken ct = default)
    {
        var request = new ProductAttributesRequest
        {
            LastId = lastId,
            Limit = limit,
            Filter = new ProductAttributesFilter
            {
                ProductId = productIds,
                OfferId = offerIds,
                Visibility = "ALL"
            }
        };
        return await PostAsync<ProductAttributesRequest, ProductAttributesResponse>("/v4/product/info/attributes", request, ct);
    }

    /// <summary>
    /// Import (create/update) products
    /// </summary>
    public async Task<ProductImportResponse> ImportProductsAsync(
        List<ProductImportItem> items,
        CancellationToken ct = default)
    {
        var request = new ProductImportRequest { Items = items };
        return await PostAsync<ProductImportRequest, ProductImportResponse>("/v3/product/import", request, ct);
    }

    /// <summary>
    /// Get import task status
    /// </summary>
    public async Task<ImportInfoResponse> GetImportInfoAsync(long taskId, CancellationToken ct = default)
    {
        var request = new ImportInfoRequest { TaskId = taskId };
        return await PostAsync<ImportInfoRequest, ImportInfoResponse>("/v1/product/import/info", request, ct);
    }

    /// <summary>
    /// Update offer_id (SKU)
    /// </summary>
    public async Task<UpdateOfferIdResponse> UpdateOfferIdAsync(
        string oldOfferId,
        string newOfferId,
        CancellationToken ct = default)
    {
        var request = new UpdateOfferIdRequest
        {
            UpdateOfferId = new List<UpdateOfferIdItem>
            {
                new() { OfferId = oldOfferId, NewOfferId = newOfferId }
            }
        };
        return await PostAsync<UpdateOfferIdRequest, UpdateOfferIdResponse>("/v1/product/update/offer-id", request, ct);
    }

    /// <summary>
    /// Archive products on Ozon
    /// </summary>
    public async Task<bool> ArchiveProductAsync(List<long> productIds, CancellationToken ct = default)
    {
        var request = new ArchiveProductRequest { ProductId = productIds };
        var response = await PostAsync<ArchiveProductRequest, ArchiveProductResponse>("/v1/product/archive", request, ct);
        return response.Result;
    }

    #endregion

    #region Price Methods

    /// <summary>
    /// Get product prices
    /// </summary>
    public async Task<PriceInfoResponse> GetPricesAsync(
        string cursor = "",
        int limit = 100,
        List<long>? productIds = null,
        List<string>? offerIds = null,
        CancellationToken ct = default)
    {
        var request = new PriceInfoRequest
        {
            Cursor = cursor,
            Limit = limit,
            Filter = new PriceInfoFilter
            {
                ProductId = productIds?.Select(id => id.ToString()).ToList(),
                OfferId = offerIds,
                Visibility = "ALL"
            }
        };
        return await PostAsync<PriceInfoRequest, PriceInfoResponse>("/v5/product/info/prices", request, ct);
    }

    /// <summary>
    /// Update product prices
    /// </summary>
    public async Task<PriceImportResponse> UpdatePricesAsync(
        List<PriceImportItem> prices,
        CancellationToken ct = default)
    {
        var request = new PriceImportRequest { Prices = prices };
        return await PostAsync<PriceImportRequest, PriceImportResponse>("/v1/product/import/prices", request, ct);
    }

    #endregion

    #region Stock Methods

    /// <summary>
    /// Get product stocks
    /// </summary>
    public async Task<StockInfoResponse> GetStocksAsync(
        string cursor = "",
        int limit = 100,
        List<long>? productIds = null,
        List<string>? offerIds = null,
        CancellationToken ct = default)
    {
        var request = new StockInfoRequest
        {
            Cursor = cursor,
            Limit = limit,
            Filter = new StockInfoFilter
            {
                ProductId = productIds?.Select(id => id.ToString()).ToList(),
                OfferId = offerIds,
                Visibility = "ALL"
            }
        };
        return await PostAsync<StockInfoRequest, StockInfoResponse>("/v4/product/info/stocks", request, ct);
    }

    /// <summary>
    /// Update product stocks
    /// </summary>
    public async Task<StockUpdateResponse> UpdateStocksAsync(
        List<StockUpdateItem> stocks,
        CancellationToken ct = default)
    {
        var request = new StockUpdateRequest { Stocks = stocks };
        return await PostAsync<StockUpdateRequest, StockUpdateResponse>("/v2/products/stocks", request, ct);
    }

    /// <summary>
    /// Get warehouse list
    /// </summary>
    public async Task<WarehouseListResponse> GetWarehousesAsync(CancellationToken ct = default)
    {
        var request = new WarehouseListRequest { Limit = 200 };
        return await PostAsync<WarehouseListRequest, WarehouseListResponse>("/v2/warehouse/list", request, ct);
    }

    #endregion

    #region Quota Methods

    /// <summary>
    /// Get upload quota (daily create/update limits and total product count).
    /// Uses Ozon API: POST /v4/product/info/limit
    /// </summary>
    public async Task<UploadQuotaResponse> GetUploadQuotaAsync(CancellationToken ct = default)
    {
        return await PostAsync<object, UploadQuotaResponse>("/v4/product/info/limit", new { }, ct);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Generic POST request method with rate limiting and 429 retry.
    /// </summary>
    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CancellationToken ct)
        where TResponse : new()
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var retryCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Wait for rate limiter
                await _rateLimiter.AcquireAsync(ct);

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
                httpRequest.Headers.Add("Client-Id", _clientId);
                httpRequest.Headers.Add("Api-Key", _apiKey);
                httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await SharedHttpClient.SendAsync(httpRequest, ct);

                // Handle 429 Too Many Requests
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    if (retryCount > MaxRetries)
                    {
                        var body429 = await response.Content.ReadAsStringAsync(ct);
                        throw new OzonApiException(
                            $"Ozon API rate limited after {MaxRetries} retries: {body429}",
                            429,
                            body429);
                    }

                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    Console.WriteLine($"[OzonApiClient] 429 rate limited on {endpoint}, retry {retryCount}/{MaxRetries}, waiting {retryAfter.TotalSeconds:F1}s");
                    await Task.Delay(retryAfter, ct);
                    continue;
                }

                var responseBody = await response.Content.ReadAsStringAsync(ct);

                // Debug logging for product attributes endpoint
                if (endpoint.Contains("/v4/product/info/attributes"))
                {
                    Console.WriteLine($"[OzonApiClient] Raw JSON response from {endpoint} (first 2000 chars):");
                    Console.WriteLine(responseBody.Length > 2000 ? responseBody.Substring(0, 2000) + "..." : responseBody);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new OzonApiException(
                        $"Ozon API error: {response.StatusCode}: {responseBody}",
                        (int)response.StatusCode,
                        responseBody);
                }

                var result = JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
                return result ?? new TResponse();
            }
            catch (OzonApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                throw new OzonApiException($"Network error: {ex.Message}", 0, null, ex);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken != ct)
            {
                throw new OzonApiException("Request timeout", 408, null, ex);
            }
            catch (JsonException ex)
            {
                throw new OzonApiException($"JSON parsing error: {ex.Message}", 0, null, ex);
            }
        }
    }

    /// <summary>
    /// Test connection by getting warehouse list
    /// </summary>
    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await GetWarehousesAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Polling Helper

    /// <summary>
    /// Poll import status until complete or timeout
    /// </summary>
    public async Task<ImportInfoResponse> PollImportStatusAsync(
        long taskId,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            var response = await GetImportInfoAsync(taskId, ct);

            // Check if all items are processed (not pending)
            // Items.All() returns true on empty collection — guard with Count > 0
            var allProcessed = response.Result.Items.Count > 0
                && response.Result.Items.All(item => item.Status != "pending");

            if (allProcessed)
            {
                return response;
            }

            await Task.Delay(pollInterval, ct);
        }

        throw new OzonApiException("Import task timeout", 408, null);
    }

    #endregion
}

/// <summary>
/// Custom exception for Ozon API errors
/// </summary>
public class OzonApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public OzonApiException(string message, int statusCode, string? responseBody, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
