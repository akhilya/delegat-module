using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrintO.Marketplace.SDK.RateLimit;
using PrintO.Marketplace.Wildberries.Models;

namespace PrintO.Marketplace.Wildberries;

public class WildberriesApiClient
{
    private const int MaxRetries = 3;

    private enum ApiDomain { Content, Prices, Marketplace }

    private static readonly HttpClient ContentClient = new()
    {
        BaseAddress = new Uri("https://content-api.wildberries.ru"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly HttpClient PricesClient = new()
    {
        BaseAddress = new Uri("https://discounts-prices-api.wildberries.ru"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly HttpClient MarketplaceClient = new()
    {
        BaseAddress = new Uri("https://marketplace-api.wildberries.ru"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly ConcurrentDictionary<string, MarketplaceRateLimiter> RateLimiters = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _apiKey;
    private readonly MarketplaceRateLimiter _rateLimiter;
    private readonly ILogger? _logger;

    public WildberriesApiClient(string apiKey, ILogger? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _logger = logger;

        var keyHash = apiKey.Length > 8 ? apiKey[..8] : apiKey;
        _rateLimiter = RateLimiters.GetOrAdd(keyHash,
            _ => new MarketplaceRateLimiter(maxRequestsPerSecond: 1));
    }

    public static MarketplaceRateLimiter? GetRateLimiterForApiKey(string apiKey)
    {
        var keyHash = apiKey.Length > 8 ? apiKey[..8] : apiKey;
        return RateLimiters.TryGetValue(keyHash, out var limiter) ? limiter : null;
    }

    #region Category Methods

    public async Task<WbParentCategoriesResponse> GetParentCategoriesAsync(CancellationToken ct = default)
    {
        return await GetAsync<WbParentCategoriesResponse>(
            ApiDomain.Content, "/content/v2/object/parent/all?locale=ru", ct);
    }

    public async Task<WbSubjectsResponse> GetSubjectsAsync(
        int? parentId = null, string? name = null, CancellationToken ct = default)
    {
        var query = "?locale=ru&limit=1000";
        if (parentId.HasValue) query += $"&parentID={parentId.Value}";
        if (!string.IsNullOrEmpty(name)) query += $"&name={Uri.EscapeDataString(name)}";
        return await GetAsync<WbSubjectsResponse>(
            ApiDomain.Content, $"/content/v2/object/all{query}", ct);
    }

    public async Task<WbCharacteristicsResponse> GetCharacteristicsAsync(
        int subjectId, CancellationToken ct = default)
    {
        return await GetAsync<WbCharacteristicsResponse>(
            ApiDomain.Content, $"/content/v2/object/charcs/{subjectId}?locale=ru", ct);
    }

    public async Task<WbDirectoryResponse> GetColorsAsync(CancellationToken ct = default)
    {
        return await GetAsync<WbDirectoryResponse>(
            ApiDomain.Content, "/content/v2/directory/colors?locale=ru", ct);
    }

    public async Task<WbStringDirectoryResponse> GetKindsAsync(CancellationToken ct = default)
    {
        return await GetAsync<WbStringDirectoryResponse>(
            ApiDomain.Content, "/content/v2/directory/kinds?locale=ru", ct);
    }

    public async Task<WbCountryDirectoryResponse> GetCountriesAsync(CancellationToken ct = default)
    {
        return await GetAsync<WbCountryDirectoryResponse>(
            ApiDomain.Content, "/content/v2/directory/countries?locale=ru", ct);
    }

    public async Task<WbStringDirectoryResponse> GetSeasonsAsync(CancellationToken ct = default)
    {
        return await GetAsync<WbStringDirectoryResponse>(
            ApiDomain.Content, "/content/v2/directory/seasons?locale=ru", ct);
    }

    public async Task<WbStringDirectoryResponse> GetVatRatesAsync(CancellationToken ct = default)
    {
        return await GetAsync<WbStringDirectoryResponse>(
            ApiDomain.Content, "/content/v2/directory/vat?locale=ru", ct);
    }

    public async Task<WbTnvedDirectoryResponse> GetTnvedAsync(int subjectId, CancellationToken ct = default)
    {
        return await GetAsync<WbTnvedDirectoryResponse>(
            ApiDomain.Content, $"/content/v2/directory/tnved?subjectID={subjectId}&locale=ru", ct);
    }

    #endregion

    #region Card Methods

    public async Task<WbCardsListResponse> GetCardsListAsync(
        WbCardsListRequest request, CancellationToken ct = default)
    {
        return await PostAsync<WbCardsListRequest, WbCardsListResponse>(
            ApiDomain.Content, "/content/v2/get/cards/list?locale=ru", request, ct);
    }

    public async Task<WbCardCreateResponse> CreateCardsAsync(
        List<WbCardCreateGroup> cards, CancellationToken ct = default)
    {
        return await PostAsync<List<WbCardCreateGroup>, WbCardCreateResponse>(
            ApiDomain.Content, "/content/v2/cards/upload", cards, ct);
    }

    public async Task<WbCardCreateResponse> UpdateCardsAsync(
        List<WbCardUpdateItem> cards, CancellationToken ct = default)
    {
        return await PostAsync<List<WbCardUpdateItem>, WbCardCreateResponse>(
            ApiDomain.Content, "/content/v2/cards/update", cards, ct);
    }

    public async Task<WbCardErrorListResponse> GetCardErrorsAsync(
        WbCardErrorListRequest request, CancellationToken ct = default)
    {
        return await PostAsync<WbCardErrorListRequest, WbCardErrorListResponse>(
            ApiDomain.Content, "/content/v2/cards/error/list?locale=ru", request, ct);
    }

    public async Task<WbCardCreateResponse> MoveToTrashAsync(
        List<int> nmIds, CancellationToken ct = default)
    {
        var request = new { nmIDs = nmIds };
        return await PostAsync<object, WbCardCreateResponse>(
            ApiDomain.Content, "/content/v2/cards/delete/trash", request, ct);
    }

    public async Task<WbCardLimitsResponse> GetCardLimitsAsync(CancellationToken ct = default)
    {
        return await GetAsync<WbCardLimitsResponse>(
            ApiDomain.Content, "/content/v2/cards/limits", ct);
    }

    public async Task<WbCardCreateResponse> AddCardsToMergeGroupAsync(
        WbCardAddToMergeRequest request, CancellationToken ct = default)
    {
        return await PostAsync<WbCardAddToMergeRequest, WbCardCreateResponse>(
            ApiDomain.Content, "/content/v2/cards/upload/add", request, ct);
    }

    public async Task<WbCardCreateResponse> MoveNmAsync(
        WbMoveNmRequest request, CancellationToken ct = default)
    {
        return await PostAsync<WbMoveNmRequest, WbCardCreateResponse>(
            ApiDomain.Content, "/content/v2/cards/moveNm", request, ct);
    }

    public async Task<WbMediaSaveResponse> SaveMediaByUrlAsync(
        WbMediaSaveRequest request, CancellationToken ct = default)
    {
        return await PostAsync<WbMediaSaveRequest, WbMediaSaveResponse>(
            ApiDomain.Content, "/content/v3/media/save", request, ct);
    }

    #endregion

    #region Price Methods

    public async Task<WbGoodsListResponse> GetGoodsWithPricesAsync(
        int limit = 1000, int offset = 0, CancellationToken ct = default)
    {
        return await GetAsync<WbGoodsListResponse>(
            ApiDomain.Prices, $"/api/v2/list/goods/filter?limit={limit}&offset={offset}", ct);
    }

    public async Task<WbGoodsListResponse> GetGoodsWithPricesByNmIdsAsync(
        List<int> nmIds, CancellationToken ct = default)
    {
        var request = new { nmList = nmIds };
        return await PostAsync<object, WbGoodsListResponse>(
            ApiDomain.Prices, "/api/v2/list/goods/filter", request, ct);
    }

    public async Task<WbSetPricesResponse> SetPricesAndDiscountsAsync(
        List<WbPriceItem> items, CancellationToken ct = default)
    {
        var request = new WbSetPricesRequest { Data = items };
        return await PostAsync<WbSetPricesRequest, WbSetPricesResponse>(
            ApiDomain.Prices, "/api/v2/upload/task", request, ct);
    }

    #endregion

    #region Stock Methods

    public async Task<List<WbWarehouse>> GetWarehousesAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<WbWarehouse>>(
            ApiDomain.Marketplace, "/api/v3/warehouses", ct);
    }

    public async Task UpdateStocksAsync(
        long warehouseId, List<WbStockItem> stocks, CancellationToken ct = default)
    {
        var request = new WbUpdateStocksRequest { Stocks = stocks };
        await PutNoContentAsync(
            ApiDomain.Marketplace, $"/api/v3/stocks/{warehouseId}", request, ct);
    }

    public async Task<WbStockLevelsResponse> GetStockLevelsAsync(
        long warehouseId, List<int> chrtIds, CancellationToken ct = default)
    {
        var request = new { chrtIds };
        return await PostAsync<object, WbStockLevelsResponse>(
            ApiDomain.Marketplace, $"/api/v3/stocks/{warehouseId}", request, ct);
    }

    #endregion

    #region HTTP Helpers

    private HttpClient GetClient(ApiDomain domain) => domain switch
    {
        ApiDomain.Content => ContentClient,
        ApiDomain.Prices => PricesClient,
        ApiDomain.Marketplace => MarketplaceClient,
        _ => throw new ArgumentOutOfRangeException(nameof(domain))
    };

    private async Task<TResponse> GetAsync<TResponse>(
        ApiDomain domain, string endpoint, CancellationToken ct) where TResponse : new()
    {
        var retryCount = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _rateLimiter.AcquireAsync(ct);

                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Add("Authorization", _apiKey);

                var response = await GetClient(domain).SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    if (retryCount > MaxRetries)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        throw new WildberriesApiException(
                            $"WB API rate limited after {MaxRetries} retries", 429, body);
                    }
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    _logger?.LogWarning("[WbApiClient] 429 on GET {Endpoint}, retry {RetryCount}/{MaxRetries}, waiting {DelaySec}s",
                        endpoint, retryCount, MaxRetries, delay.TotalSeconds.ToString("F0"));
                    await Task.Delay(delay, ct);
                    continue;
                }

                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    throw new WildberriesApiException(
                        $"WB API error: {response.StatusCode}: {responseBody}",
                        (int)response.StatusCode, responseBody);
                }

                return JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions) ?? new TResponse();
            }
            catch (WildberriesApiException) { throw; }
            catch (HttpRequestException ex)
            {
                throw new WildberriesApiException($"Network error: {ex.Message}", 0, null, ex);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken != ct)
            {
                throw new WildberriesApiException("Request timeout", 408, null, ex);
            }
            catch (JsonException ex)
            {
                throw new WildberriesApiException($"JSON parsing error: {ex.Message}", 0, null, ex);
            }
        }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        ApiDomain domain, string endpoint, TRequest body, CancellationToken ct) where TResponse : new()
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var retryCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _rateLimiter.AcquireAsync(ct);

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("Authorization", _apiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await GetClient(domain).SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    if (retryCount > MaxRetries)
                    {
                        var body429 = await response.Content.ReadAsStringAsync(ct);
                        throw new WildberriesApiException(
                            $"WB API rate limited after {MaxRetries} retries", 429, body429);
                    }
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    _logger?.LogWarning("[WbApiClient] 429 on POST {Endpoint}, retry {RetryCount}/{MaxRetries}, waiting {DelaySec}s",
                        endpoint, retryCount, MaxRetries, delay.TotalSeconds.ToString("F0"));
                    await Task.Delay(delay, ct);
                    continue;
                }

                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    throw new WildberriesApiException(
                        $"WB API error: {response.StatusCode}: {responseBody}",
                        (int)response.StatusCode, responseBody);
                }

                return JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions) ?? new TResponse();
            }
            catch (WildberriesApiException) { throw; }
            catch (HttpRequestException ex)
            {
                throw new WildberriesApiException($"Network error: {ex.Message}", 0, null, ex);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken != ct)
            {
                throw new WildberriesApiException("Request timeout", 408, null, ex);
            }
            catch (JsonException ex)
            {
                throw new WildberriesApiException($"JSON parsing error: {ex.Message}", 0, null, ex);
            }
        }
    }

    private async Task PutNoContentAsync<TRequest>(
        ApiDomain domain, string endpoint, TRequest body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var retryCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _rateLimiter.AcquireAsync(ct);

                var request = new HttpRequestMessage(HttpMethod.Put, endpoint);
                request.Headers.Add("Authorization", _apiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await GetClient(domain).SendAsync(request, ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    retryCount++;
                    if (retryCount > MaxRetries)
                    {
                        var body429 = await response.Content.ReadAsStringAsync(ct);
                        throw new WildberriesApiException(
                            $"WB API rate limited after {MaxRetries} retries", 429, body429);
                    }
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                    _logger?.LogWarning("[WbApiClient] 429 on PUT {Endpoint}, retry {RetryCount}/{MaxRetries}, waiting {DelaySec}s",
                        endpoint, retryCount, MaxRetries, delay.TotalSeconds.ToString("F0"));
                    await Task.Delay(delay, ct);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NoContent) return;

                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    throw new WildberriesApiException(
                        $"WB API error: {response.StatusCode}: {responseBody}",
                        (int)response.StatusCode, responseBody);
                }
            }
            catch (WildberriesApiException) { throw; }
            catch (HttpRequestException ex)
            {
                throw new WildberriesApiException($"Network error: {ex.Message}", 0, null, ex);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken != ct)
            {
                throw new WildberriesApiException("Request timeout", 408, null, ex);
            }
        }
    }

    #endregion
}
