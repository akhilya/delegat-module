using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Marketplace.YandexMarket;

public class YandexMarketApiClient
{
    private const string BaseUrl = "https://api.partner.market.yandex.ru";
    private const int MaxRetries = 3;

    private static readonly HttpClient SharedHttpClient = new()
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly ConcurrentDictionary<string, MarketplaceRateLimiter> RateLimiters = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _apiKey;
    private readonly MarketplaceRateLimiter _rateLimiter;

    public YandexMarketApiClient(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _rateLimiter = RateLimiters.GetOrAdd(apiKey, _ => new MarketplaceRateLimiter(maxRequestsPerSecond: 50));
    }

    public static MarketplaceRateLimiter? GetRateLimiterForApiKey(string apiKey)
        => RateLimiters.TryGetValue(apiKey, out var limiter) ? limiter : null;

    #region Campaigns & Auth

    public Task<ApiResponse<CampaignsResultDto>> GetCampaignsAsync(CancellationToken ct = default)
        => GetAsync<ApiResponse<CampaignsResultDto>>("/v2/campaigns", ct);

    public Task<ApiResponse<AuthTokenResultDto>> GetAuthTokenInfoAsync(CancellationToken ct = default)
        => PostAsync<object, ApiResponse<AuthTokenResultDto>>("/v2/auth/token", new { }, ct);

    #endregion

    #region Categories

    public Task<ApiResponse<CategoryDto>> GetCategoriesTreeAsync(CancellationToken ct = default)
        => PostAsync<CategoriesTreeRequest, ApiResponse<CategoryDto>>(
            "/v2/categories/tree",
            new CategoriesTreeRequest { Language = "RU" },
            ct);

    public Task<ApiResponse<CategoryContentParametersDto>> GetCategoryParametersAsync(
        long categoryId,
        long? businessId = null,
        CancellationToken ct = default)
    {
        var query = businessId.HasValue ? $"?businessId={businessId.Value}" : "";
        return PostAsync<object, ApiResponse<CategoryContentParametersDto>>(
            $"/v2/category/{categoryId}/parameters{query}",
            new { },
            ct);
    }

    #endregion

    #region Offers

    public Task<ApiResponse<OfferMappingsResultDto>> GetOfferMappingsAsync(
        long businessId,
        List<string>? offerIds = null,
        string? pageToken = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var query = $"?limit={limit}";
        if (!string.IsNullOrEmpty(pageToken))
            query += $"&pageToken={Uri.EscapeDataString(pageToken)}";

        var body = offerIds is { Count: > 0 }
            ? new GetOfferMappingsRequest { OfferIds = offerIds }
            : new GetOfferMappingsRequest();

        return PostAsync<GetOfferMappingsRequest, ApiResponse<OfferMappingsResultDto>>(
            $"/v2/businesses/{businessId}/offer-mappings{query}",
            body,
            ct);
    }

    public Task<ApiResponse<UpdateOfferMappingsResultDto>> UpdateOfferMappingsAsync(
        long businessId,
        List<UpdateOfferMappingDto> mappings,
        CancellationToken ct = default)
        => PostAsync<UpdateOfferMappingsRequest, ApiResponse<UpdateOfferMappingsResultDto>>(
            $"/v2/businesses/{businessId}/offer-mappings/update",
            new UpdateOfferMappingsRequest { OfferMappings = mappings },
            ct);

    public Task<ApiResponse<object>> ArchiveOffersAsync(
        long businessId,
        List<string> offerIds,
        CancellationToken ct = default)
        => PostAsync<ArchiveOffersRequest, ApiResponse<object>>(
            $"/v2/businesses/{businessId}/offer-mappings/archive",
            new ArchiveOffersRequest { OfferIds = offerIds },
            ct);

    #endregion

    #region Prices

    public Task<ApiResponse<OfferPricesResultDto>> GetDefaultPricesAsync(
        long businessId,
        List<string>? offerIds = null,
        CancellationToken ct = default)
        => PostAsync<GetOfferPricesRequest, ApiResponse<OfferPricesResultDto>>(
            $"/v2/businesses/{businessId}/offer-prices",
            new GetOfferPricesRequest { OfferIds = offerIds },
            ct);

    public Task<ApiResponse<object>> UpdateBusinessPricesAsync(
        long businessId,
        List<UpdateBusinessPriceDto> offers,
        CancellationToken ct = default)
        => PostAsync<UpdateBusinessPricesRequest, ApiResponse<object>>(
            $"/v2/businesses/{businessId}/offer-prices/updates",
            new UpdateBusinessPricesRequest { Offers = offers },
            ct);

    #endregion

    #region Stocks

    public Task<ApiResponse<StocksResultDto>> GetStocksAsync(
        long campaignId,
        List<string>? offerIds = null,
        CancellationToken ct = default)
        => PostAsync<GetStocksRequest, ApiResponse<StocksResultDto>>(
            $"/v2/campaigns/{campaignId}/offers/stocks",
            new GetStocksRequest { OfferIds = offerIds },
            ct);

    public Task<ApiResponse<object>> UpdateStocksAsync(
        long campaignId,
        List<UpdateStockDto> skus,
        CancellationToken ct = default)
        => PutAsync<UpdateStocksRequest, ApiResponse<object>>(
            $"/v2/campaigns/{campaignId}/offers/stocks",
            new UpdateStocksRequest { Skus = skus },
            ct);

    #endregion

    #region HTTP

    private Task<TResponse> GetAsync<TResponse>(string endpoint, CancellationToken ct)
        => SendAsync<TResponse>(HttpMethod.Get, endpoint, null, ct);

    private Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest body, CancellationToken ct)
        => SendAsync<TResponse>(HttpMethod.Post, endpoint, body, ct);

    private Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest body, CancellationToken ct)
        => SendAsync<TResponse>(HttpMethod.Put, endpoint, body, ct);

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string endpoint,
        object? body,
        CancellationToken ct)
    {
        var retryCount = 0;

        while (true)
        {
            await _rateLimiter.AcquireAsync(ct);

            using var httpRequest = new HttpRequestMessage(method, endpoint);
            httpRequest.Headers.Add("Api-Key", _apiKey);

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = await SharedHttpClient.SendAsync(httpRequest, ct);

            if (response.StatusCode is HttpStatusCode.TooManyRequests
                or (HttpStatusCode)420)
            {
                retryCount++;
                if (retryCount > MaxRetries)
                {
                    var body420 = await response.Content.ReadAsStringAsync(ct);
                    throw new YandexMarketApiException(
                        $"Yandex Market API rate limited after {MaxRetries} retries",
                        (int)response.StatusCode,
                        body420);
                }

                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * retryCount);
                await Task.Delay(retryAfter, ct);
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new YandexMarketApiException(
                    $"Yandex Market API error: {response.StatusCode}: {responseBody}",
                    (int)response.StatusCode,
                    responseBody);
            }

            if (typeof(TResponse) == typeof(object) && string.IsNullOrWhiteSpace(responseBody))
                return (TResponse)(object)new object();

            try
            {
                var result = JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);
                return result ?? Activator.CreateInstance<TResponse>();
            }
            catch (JsonException ex)
            {
                throw new YandexMarketApiException($"JSON parsing error: {ex.Message}", 0, responseBody, ex);
            }
        }
    }

    #endregion
}