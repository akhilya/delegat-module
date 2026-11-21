using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Marketplace.SDK;

namespace Delegat.Services.Integration;

internal enum IntegrationProviderKind
{
    Ozon,
    Wildberries,
    YandexMarket,
    Avito,
    OpenCart,
    WooCommerce,
    Medusa
}

internal enum IntegrationProviderEnvironment
{
    Production,
    Sandbox
}

internal enum WildberriesTokenClass
{
    Unverified = 0, // Conservatively treated as Base.
    Base = 1,
    BaseWithSecret = 2,
    Personal = 3,
    Service = 4
}

internal enum YandexResourceTier
{
    Unverified = 0, // Conservatively treated as None.
    None = 1,
    Medium = 2
}

/// <summary>
/// Host-owned, verified provider/account binding. SDK request policy hints are
/// never authoritative for seller identity, destination, effect, or costs.
/// </summary>
internal sealed record IntegrationHostRouteBinding(
    IntegrationProviderKind Provider,
    IntegrationProviderEnvironment Environment,
    Uri AllowedOrigin,
    string? VerifiedSellerId = null,
    WildberriesTokenClass WbTokenClass = WildberriesTokenClass.Unverified,
    YandexResourceTier YandexTier = YandexResourceTier.Unverified,
    IReadOnlyList<string>? EntityKeys = null,
    long? ExpectedMaxResponseItems = null,
    int ResponseLimitBytes = 2 * 1024 * 1024);

/// <summary>
/// Bounded, evidence-backed outbound route policy catalog derived from the
/// audited 2026-11-20 provider rate ledger. Unknown routes fail closed.
/// </summary>
internal static partial class IntegrationRoutePolicyCatalog
{
    internal const int YandexMaxBodyBytes = 512 * 1024;
    internal const string OzonResetTimeZoneId = "Europe/Moscow";
    internal const int OzonResetHourOfDay = 3;

    internal static string SellerScopeSegment(IntegrationHostRouteBinding binding) =>
        string.IsNullOrWhiteSpace(binding.VerifiedSellerId)
            ? "unverified-shared"
            : $"seller:{binding.VerifiedSellerId.Trim()}";

    internal static string OzonDailyImportScopeKey(IntegrationHostRouteBinding binding) =>
        $"ozon:{Env(binding)}:{SellerScopeSegment(binding)}:daily-import-items";

    internal static IntegrationOutboundPolicy? Resolve(
        IntegrationHostRouteBinding binding,
        IntegrationTransportRequest request)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Url.IsAbsoluteUri || !binding.AllowedOrigin.IsAbsoluteUri ||
            !string.Equals(request.Url.Scheme, binding.AllowedOrigin.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.Url.Host, binding.AllowedOrigin.Host, StringComparison.OrdinalIgnoreCase) ||
            request.Url.Port != binding.AllowedOrigin.Port)
            return null;

        var method = request.Method.ToUpperInvariant();
        var path = NormalizePath(request.Url.AbsolutePath);

        return binding.Provider switch
        {
            IntegrationProviderKind.Wildberries => ResolveWildberries(binding, method, path),
            IntegrationProviderKind.Ozon => ResolveOzon(binding, method, path, request.Body),
            IntegrationProviderKind.YandexMarket => ResolveYandexMarket(binding, method, path, request.Body),
            IntegrationProviderKind.Avito => ResolveAvito(binding, method, path),
            IntegrationProviderKind.OpenCart or IntegrationProviderKind.WooCommerce or IntegrationProviderKind.Medusa
                => ResolveDelegatBridge(binding, method, path),
            _ => null
        };
    }

    // ---------- Wildberries (audit ledger lines 886-890, 928-979) ----------

    private static IntegrationOutboundPolicy? ResolveWildberries(
        IntegrationHostRouteBinding binding, string method, string path)
    {
        var seller = SellerScopeSegment(binding);
        var env = Env(binding);
        var elevated = binding.WbTokenClass is WildberriesTokenClass.Personal
            or WildberriesTokenClass.Service
            or WildberriesTokenClass.BaseWithSecret;
        var prefix = $"wb:{env}:{seller}";

        // Unmatched shipment routes from audit ledger (lines 968, 970, 973) fail closed.
        if ((method == "PATCH" && WbSupplyOrdersRegex().IsMatch(path)) ||
            (method == "GET" && WbSupplyOrderIdsRegex().IsMatch(path)) ||
            (method == "PATCH" && WbSupplyTrbxBoxRegex().IsMatch(path)))
            return null;

        // Content ordinary read/move methods: 100/min, 600ms, burst 5.
        if ((method == "GET" && (path is "/content/v2/object/parent/all" or "/content/v2/object/all"
                or "/content/v2/directory/countries" or "/content/v2/directory/tnved"
                || WbObjectCharcsRegex().IsMatch(path))) ||
            (method == "POST" && path == "/content/v2/cards/moveNm"))
        {
            var isEffectful = method == "POST";
            return WbPolicy(isEffectful,
                [TokenBucket($"{prefix}:content:{method}:{NormalizeWbBucket(path)}", TimeSpan.FromMilliseconds(600), 5)],
                binding.ResponseLimitBytes);
        }

        // Token-class-differentiated Content directory/trash/limits/media:
        // Elevated: 100/min, 600ms, burst 5; Base/Unverified: 2/hr, 30m, burst 1.
        if ((method == "GET" && path is "/content/v2/directory/colors" or "/content/v2/directory/kinds"
                or "/content/v2/directory/seasons" or "/content/v2/directory/vat" or "/content/v2/cards/limits") ||
            (method == "POST" && path is "/content/v2/cards/delete/trash" or "/content/v3/media/save"))
        {
            var isEffectful = method == "POST";
            var bucket = elevated
                ? TokenBucket($"{prefix}:content:{method}:{path}", TimeSpan.FromMilliseconds(600), 5)
                : TokenBucket($"{prefix}:content-base:{method}:{path}", TimeSpan.FromMinutes(30), 1);
            return WbPolicy(isEffectful, [bucket], binding.ResponseLimitBytes);
        }

        // Card list: 80/min, 750ms, burst 5.
        if (method == "POST" && path == "/content/v2/get/cards/list")
            return WbPolicy(false, [TokenBucket($"{prefix}:content:cards-list", TimeSpan.FromMilliseconds(750), 5)], binding.ResponseLimitBytes);

        // Card upload / update: 10/min, 6s, burst 5.
        if (method == "POST" && path is "/content/v2/cards/upload" or "/content/v2/cards/update")
            return WbPolicy(true, [TokenBucket($"{prefix}:content:{path}", TimeSpan.FromSeconds(6), 5)], binding.ResponseLimitBytes);

        // Card upload/add: Elevated 10/min (6s, burst 5); Base/Unverified 1/2h (2h, burst 1).
        if (method == "POST" && path == "/content/v2/cards/upload/add")
        {
            var bucket = elevated
                ? TokenBucket($"{prefix}:content:cards-upload-add", TimeSpan.FromSeconds(6), 5)
                : TokenBucket($"{prefix}:content-base:cards-upload-add", TimeSpan.FromHours(2), 1);
            return WbPolicy(true, [bucket], binding.ResponseLimitBytes);
        }

        // Card error list: 8/min, 7500ms, burst 5.
        if (method == "POST" && path == "/content/v2/cards/error/list")
            return WbPolicy(false, [TokenBucket($"{prefix}:content:cards-error-list", TimeSpan.FromMilliseconds(7500), 5)], binding.ResponseLimitBytes);

        // Prices & goods filter: Elevated 10/6s (600ms, burst 5); Base/Unverified 4/hr (15m, burst 1).
        if ((method is "GET" or "POST" && path == "/api/v2/list/goods/filter") ||
            (method == "POST" && path is "/api/v2/upload/task" or "/api/v2/upload/task/size"))
        {
            var isEffectful = path.StartsWith("/api/v2/upload/", StringComparison.Ordinal);
            var bucket = elevated
                ? TokenBucket($"{prefix}:prices:{path}", TimeSpan.FromMilliseconds(600), 5)
                : TokenBucket($"{prefix}:prices-base:{path}", TimeSpan.FromMinutes(15), 1);
            return WbPolicy(isEffectful, [bucket], binding.ResponseLimitBytes);
        }

        // Calendar promotions: list/upload differ by token class; nomenclatures is 10/6s without Base row.
        if ((method == "GET" && path == "/api/v1/calendar/promotions") ||
            (method == "POST" && path == "/api/v1/calendar/promotions/upload"))
        {
            var isEffectful = method == "POST";
            var bucket = elevated
                ? TokenBucket($"{prefix}:calendar:{path}", TimeSpan.FromMilliseconds(600), 5)
                : TokenBucket($"{prefix}:calendar-base:{path}", TimeSpan.FromHours(1), 1);
            return WbPolicy(isEffectful, [bucket], binding.ResponseLimitBytes);
        }
        if (method == "GET" && path == "/api/v1/calendar/promotions/nomenclatures")
            return WbPolicy(false, [TokenBucket($"{prefix}:calendar:nomenclatures", TimeSpan.FromMilliseconds(600), 5)], binding.ResponseLimitBytes);

        // Marketplace / FBS (warehouses, stocks, orders, supplies, trbx):
        // Production: 300/min (200ms, burst 20) or 100/min (600ms, burst 20) for order cancel, with 4XX cost = 10 requests.
        // Sandbox: 1/sec aggregate per Marketplace category.
        var (fbsMatched, fbsEffectful, fbsIsCancel) = MatchWbMarketplaceFbs(method, path);
        if (fbsMatched)
        {
            var scopeKey = binding.Environment == IntegrationProviderEnvironment.Sandbox
                ? $"{prefix}:marketplace-sandbox-aggregate"
                : $"{prefix}:marketplace:{(fbsIsCancel ? "orders-cancel" : "fbs")}";
            var scope = binding.Environment == IntegrationProviderEnvironment.Sandbox
                ? TokenBucket(scopeKey, TimeSpan.FromSeconds(1), 1)
                : TokenBucket(scopeKey, fbsIsCancel ? TimeSpan.FromMilliseconds(600) : TimeSpan.FromMilliseconds(200), 20);

            return new IntegrationOutboundPolicy(
                fbsEffectful,
                [scope],
                binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Wildberries,
                SettleResponse: (status, _, _) =>
                    status is >= 400 and < 500
                        ? new Dictionary<string, IntegrationScopeSettlement>(StringComparer.Ordinal)
                        {
                            // WB Marketplace/FBS documented rule: 4XX costs 10 requests.
                            [scopeKey] = new IntegrationScopeSettlement(ActualCost: 10)
                        }
                        : EmptySettlements);
        }

        return null;
    }

    private static IntegrationOutboundPolicy WbPolicy(
        bool isEffectful, IReadOnlyList<IntegrationScope> scopes, int responseLimitBytes) =>
        new(isEffectful, scopes, responseLimitBytes, HeaderProfile: IntegrationVendorHeaderProfile.Wildberries);

    private static (bool Matched, bool IsEffectful, bool IsCancel) MatchWbMarketplaceFbs(string method, string path)
    {
        if (method == "PATCH" && WbOrderCancelRegex().IsMatch(path))
            return (true, true, true);

        if ((method == "GET" && (path is "/api/v3/warehouses" or "/api/v3/offices" or "/api/v3/orders"
                or "/api/v3/orders/new" or "/api/v3/supplies"
                || WbSupplyIdRegex().IsMatch(path)
                || WbSupplyTrbxRegex().IsMatch(path)
                || WbSupplyBarcodeRegex().IsMatch(path))) ||
            (method == "POST" && (path is "/api/v3/orders/status" or "/api/v3/orders/stickers"
                || WbStocksRegex().IsMatch(path)
                || WbSupplyTrbxStickersRegex().IsMatch(path))))
            return (true, false, false);

        if ((method == "PUT" && WbStocksRegex().IsMatch(path)) ||
            (method == "POST" && (path == "/api/v3/supplies" || WbSupplyTrbxRegex().IsMatch(path))) ||
            (method == "PATCH" && WbSupplyDeliverRegex().IsMatch(path)))
            return (true, true, false);

        return (false, false, false);
    }

    // ---------- Ozon (audit ledger lines 882-885) ----------

    private static IntegrationOutboundPolicy? ResolveOzon(
        IntegrationHostRouteBinding binding, string method, string path, string? body)
    {
        if (method != "POST")
            return null;

        var seller = SellerScopeSegment(binding);
        var env = Env(binding);
        var generalKey = $"ozon:{env}:{seller}:general";
        var generalScope = new IntegrationScope(
            generalKey, TimeSpan.Zero, MaxConcurrency: 0,
            WindowKind: IntegrationScopeWindowKind.FixedWindow,
            Allowance: 50, Period: TimeSpan.FromSeconds(1), ReservedCost: 1);

        // /v2/products/stocks: 80 calls/min/seller, max 100 pairs/request, 30s per (product, warehouse) pair.
        if (path == "/v2/products/stocks")
        {
            var pairs = ExtractOzonStockPairs(binding, body);
            if (pairs.Count is 0 or > 100)
                return null;

            var scopes = new List<IntegrationScope>(2 + pairs.Count)
            {
                generalScope,
                new IntegrationScope(
                    $"ozon:{env}:{seller}:stocks-method", TimeSpan.Zero, MaxConcurrency: 0,
                    WindowKind: IntegrationScopeWindowKind.FixedWindow,
                    Allowance: 80, Period: TimeSpan.FromMinutes(1), ReservedCost: 1)
            };
            foreach (var pair in pairs)
            {
                scopes.Add(new IntegrationScope(
                    $"ozon:{env}:{seller}:stock-pair:{pair}", TimeSpan.FromSeconds(30), MaxConcurrency: 1));
            }
            return new IntegrationOutboundPolicy(true, scopes, binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Ozon);
        }

        // /v1/product/import/prices: 10 price changes/hour/product.
        if (path == "/v1/product/import/prices")
        {
            var products = ExtractOzonPriceProducts(binding, body);
            if (products.Count == 0)
                return null;

            var scopes = new List<IntegrationScope>(1 + products.Count) { generalScope };
            foreach (var product in products)
            {
                scopes.Add(new IntegrationScope(
                    $"ozon:{env}:{seller}:price-product:{product}", TimeSpan.Zero, MaxConcurrency: 0,
                    WindowKind: IntegrationScopeWindowKind.FixedWindow,
                    Allowance: 10, Period: TimeSpan.FromHours(1), ReservedCost: 1));
            }
            return new IntegrationOutboundPolicy(true, scopes, binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Ozon);
        }

        // /v3/product/import (and /v2/product/import): max 100 items/request, daily reset 03:00 Europe/Moscow.
        if (path is "/v3/product/import" or "/v2/product/import")
        {
            var itemCount = CountJsonArrayProperty(body, "items");
            if (itemCount is <= 0 or > 100)
                return null;

            var dailyScope = new IntegrationScope(
                OzonDailyImportScopeKey(binding), TimeSpan.Zero, MaxConcurrency: 0,
                WindowKind: IntegrationScopeWindowKind.CalendarReset,
                Allowance: null, // Dynamic seller limit populated via RecordObservedQuotaAsync (/v4/product/info/limit).
                ReservedCost: itemCount,
                ResetTimeZoneId: OzonResetTimeZoneId,
                ResetHourOfDay: OzonResetHourOfDay);

            return new IntegrationOutboundPolicy(true, [generalScope, dailyScope], binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Ozon);
        }

        // Documented order, async label (D2), and promotion mutation routes under general 50/s account budget.
        if (path is "/v3/posting/fbs/package-label/create"
            or "/v4/posting/fbs/ship"
            or "/v2/posting/fbs/cancel"
            or "/v1/actions/products/activate"
            or "/v1/actions/products/deactivate")
        {
            return new IntegrationOutboundPolicy(true, [generalScope], binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Ozon);
        }

        // Documented auxiliary/polling/read routes under general 50/s account budget.
        if (path is "/v4/product/info/limit" or "/v1/product/import/info" or "/v3/product/list"
            or "/v3/product/info/list" or "/v4/product/info/attributes"
            or "/v1/description-category/tree" or "/v1/description-category/attribute"
            or "/v1/description-category/attribute/values" or "/v1/warehouse/list"
            or "/v3/posting/fbs/get" or "/v3/posting/fbs/list"
            or "/v2/posting/fbs/package-label/get"
            or "/v1/actions" or "/v1/actions/products")
        {
            return new IntegrationOutboundPolicy(false, [generalScope], binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Ozon);
        }

        return null;
    }

    // ---------- Yandex Market (audit ledger lines 891-893, 901-927) ----------

    private static IntegrationOutboundPolicy? ResolveYandexMarket(
        IntegrationHostRouteBinding binding, string method, string path, string? body)
    {
        var seller = SellerScopeSegment(binding);
        var env = Env(binding);
        var medium = binding.YandexTier == YandexResourceTier.Medium;

        // Conservative resolution of conflicting concurrency docs (4 vs 6): always 4 parallel.
        var concurrencyScopeKey = ResolveYandexConcurrencyScopeKey(env, seller, path);
        var concurrencyScope = new IntegrationScope(concurrencyScopeKey, TimeSpan.Zero, MaxConcurrency: 4);

        var matched = MatchYandexRoute(method, path);
        if (matched is null)
            return null;

        var (routeId, isEffectful, costMode, costProperty, noneAllowance, mediumAllowance, period) = matched.Value;
        var allowance = medium ? mediumAllowance : noneAllowance;
        long reservedCost = costMode switch
        {
            YandexCostMode.RequestCount => 1,
            YandexCostMode.RequestArray => CountJsonArrayProperty(body, costProperty!),
            YandexCostMode.ResponseArray => binding.ExpectedMaxResponseItems is > 0 ? binding.ExpectedMaxResponseItems.Value : 200,
            _ => 0
        };
        if (reservedCost <= 0)
            return null;

        var resourceScopeKey = $"yandex:{env}:{seller}:res:{routeId}";
        var resourceScope = new IntegrationScope(
            resourceScopeKey, TimeSpan.Zero, MaxConcurrency: 0,
            WindowKind: IntegrationScopeWindowKind.FixedWindow,
            Allowance: allowance, Period: period, ReservedCost: reservedCost);

        return new IntegrationOutboundPolicy(
            isEffectful,
            [concurrencyScope, resourceScope],
            binding.ResponseLimitBytes,
            RequestBodyLimitBytes: YandexMaxBodyBytes,
            HeaderProfile: IntegrationVendorHeaderProfile.YandexMarket,
            SettleResponse: (status, respBody, _) =>
            {
                // Documented rule: 2xx and 4xx consume resource cost; 5xx do not.
                if (status >= 500 || status == 0)
                {
                    return new Dictionary<string, IntegrationScopeSettlement>(StringComparer.Ordinal)
                    {
                        [resourceScopeKey] = new IntegrationScopeSettlement(ActualCost: 0)
                    };
                }
                if (costMode == YandexCostMode.ResponseArray && respBody is not null)
                {
                    var actual = CountYandexResponseElements(respBody, costProperty!);
                    if (actual >= 0)
                    {
                        return new Dictionary<string, IntegrationScopeSettlement>(StringComparer.Ordinal)
                        {
                            [resourceScopeKey] = new IntegrationScopeSettlement(ActualCost: actual)
                        };
                    }
                }
                return EmptySettlements;
            });
    }

    private enum YandexCostMode { RequestCount, RequestArray, ResponseArray }

    private static (string RouteId, bool IsEffectful, YandexCostMode CostMode, string? CostProperty,
        long NoneAllowance, long MediumAllowance, TimeSpan Period)? MatchYandexRoute(string method, string path)
    {
        var hour = TimeSpan.FromHours(1);
        var min = TimeSpan.FromMinutes(1);

        if (method == "GET" && path == "/v2/campaigns")
            return ("campaigns-list", false, YandexCostMode.RequestCount, null, 1000, 1000, hour);
        if (method == "POST" && path == "/v2/auth/token")
            return ("auth-token", false, YandexCostMode.RequestCount, null, 100, 100, hour);
        if (method == "POST" && path == "/v2/categories/tree")
            return ("categories-tree", false, YandexCostMode.RequestCount, null, 50, 100, hour);
        if (method == "POST" && YandexCategoryParamsRegex().IsMatch(path))
            return ("category-parameters", false, YandexCostMode.RequestCount, null, 100, 100, min);
        if (method == "POST" && YandexBizOfferMappingsRegex().IsMatch(path))
            return ("offer-mappings-read", false, YandexCostMode.RequestCount, null, 100, 600, min);
        if (method == "POST" && YandexBizOfferMappingsUpdateRegex().IsMatch(path))
            return ("offer-mappings-update", true, YandexCostMode.RequestArray, "offerMappings", 5000, 10000, min);
        if (method == "POST" && YandexBizOfferCardsUpdateRegex().IsMatch(path))
            return ("offer-cards-update", true, YandexCostMode.RequestArray, "offersContent", 5000, 10000, min);
        if (method == "POST" && YandexBizOfferCardsRegex().IsMatch(path))
            return ("offer-cards-read", false, YandexCostMode.RequestCount, null, 100, 600, min);
        if (method == "POST" && YandexBizOfferMappingsArchiveRegex().IsMatch(path))
            return ("offer-mappings-archive", true, YandexCostMode.RequestArray, "offerIds", 5000, 10000, min);
        if (method == "POST" && YandexBizOfferPricesRegex().IsMatch(path))
            return ("offer-prices-read", false, YandexCostMode.ResponseArray, "result.offers", 5000, 10000, min);
        if (method == "POST" && YandexBizOfferPricesUpdatesRegex().IsMatch(path))
            return ("offer-prices-updates", true, YandexCostMode.RequestArray, "offers", 10000, 10000, min);
        if (method == "POST" && YandexCampaignStocksRegex().IsMatch(path))
            return ("campaign-stocks-read", false, YandexCostMode.ResponseArray, "result.warehouses.offers", 100000, 100000, min);
        if (method == "PUT" && YandexCampaignStocksRegex().IsMatch(path))
            return ("campaign-stocks-update", true, YandexCostMode.RequestArray, "skus", 100000, 100000, min);
        if (method == "GET" && YandexCampaignOrdersRegex().IsMatch(path))
            return ("campaign-orders-list", false, YandexCostMode.RequestCount, null, 10000, 10000, hour);
        if (method == "GET" && YandexCampaignOrderDetailRegex().IsMatch(path))
            return ("campaign-order-detail", false, YandexCostMode.RequestCount, null, 10000, 10000, hour);
        if (method == "PUT" && YandexCampaignOrderStatusRegex().IsMatch(path))
            return ("campaign-order-status", true, YandexCostMode.RequestCount, null, 10000, 10000, hour);
        if (method == "GET" && YandexCampaignOrderLabelsRegex().IsMatch(path))
            return ("campaign-order-labels", false, YandexCostMode.RequestCount, null, 10000, 10000, hour);
        if (method == "POST" && YandexBizPromosRegex().IsMatch(path))
            return ("promos-list", false, YandexCostMode.RequestCount, null, 1000, 1000, hour);
        if (method == "POST" && YandexBizPromosUpdateRegex().IsMatch(path))
            return ("promos-offers-update", true, YandexCostMode.RequestCount, null, 5000, 5000, hour);
        if (method == "POST" && YandexBizPromosDeleteRegex().IsMatch(path))
            return ("promos-offers-delete", true, YandexCostMode.RequestCount, null, 5000, 5000, hour);

        return null;
    }

    private static string ResolveYandexConcurrencyScopeKey(string env, string seller, string path)
    {
        var camp = YandexCampaignIdRegex().Match(path);
        if (camp.Success)
            return $"yandex:{env}:{seller}:conc:campaign:{camp.Groups[1].Value}";
        var biz = YandexBusinessIdRegex().Match(path);
        if (biz.Success)
            return $"yandex:{env}:{seller}:conc:business:{biz.Groups[1].Value}";
        return $"yandex:{env}:{seller}:conc:account";
    }

    // ---------- Avito (audit ledger lines 880-881) ----------

    private static IntegrationOutboundPolicy? ResolveAvito(
        IntegrationHostRouteBinding binding, string method, string path)
    {
        var seller = SellerScopeSegment(binding);
        var env = Env(binding);
        var prefix = $"avito:{env}:{seller}";

        // OAuth token call is explicitly admitted, never hidden auxiliary traffic.
        if (method == "POST" && path == "/token")
        {
            return new IntegrationOutboundPolicy(false,
                [new IntegrationScope($"{prefix}:auth", TimeSpan.Zero, MaxConcurrency: 1)],
                binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Avito);
        }

        // GET /core/v1/items: 25 requests/minute.
        if (method == "GET" && path == "/core/v1/items")
        {
            return new IntegrationOutboundPolicy(false,
                [new IntegrationScope($"{prefix}:items-list", TimeSpan.Zero, MaxConcurrency: 1,
                    WindowKind: IntegrationScopeWindowKind.FixedWindow,
                    Allowance: 25, Period: TimeSpan.FromMinutes(1), ReservedCost: 1)],
                binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Avito);
        }

        // POST /core/v1/items/{itemId}/update_price: 150 requests/minute.
        if (method == "POST" && AvitoUpdatePriceRegex().IsMatch(path))
        {
            return new IntegrationOutboundPolicy(true,
                [new IntegrationScope($"{prefix}:items-price", TimeSpan.Zero, MaxConcurrency: 1,
                    WindowKind: IntegrationScopeWindowKind.FixedWindow,
                    Allowance: 150, Period: TimeSpan.FromMinutes(1), ReservedCost: 1)],
                binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Avito);
        }

        // Documented account/autoload reads: conservative serialized scope until numeric limits are established.
        if (method == "GET" && (path == "/core/v1/accounts/self" || path.StartsWith("/autoload/v2/", StringComparison.Ordinal)))
        {
            return new IntegrationOutboundPolicy(false,
                [new IntegrationScope($"{prefix}:auxiliary", TimeSpan.Zero, MaxConcurrency: 1)],
                binding.ResponseLimitBytes,
                HeaderProfile: IntegrationVendorHeaderProfile.Avito);
        }

        return null;
    }

    // ---------- Delegat Bridges: OpenCart, WooCommerce, Medusa (audit ledger lines 894-896) ----------

    private static IntegrationOutboundPolicy? ResolveDelegatBridge(
        IntegrationHostRouteBinding binding, string method, string path)
    {
        // Bridge quotas are explicitly unknown: use protective local serialization and observed
        // Retry-After cooldown without inheriting or advertising native platform quotas.
        var allowed = binding.Provider switch
        {
            IntegrationProviderKind.OpenCart => path is "/index.php" || path.StartsWith("/api/delegat/", StringComparison.Ordinal),
            IntegrationProviderKind.WooCommerce => path.StartsWith("/wp-json/delegat-bridge/", StringComparison.Ordinal),
            IntegrationProviderKind.Medusa => path.StartsWith("/admin/delegat/", StringComparison.Ordinal)
                || path.StartsWith("/delegat/", StringComparison.Ordinal),
            _ => false
        };
        if (!allowed)
            return null;

        var isEffectful = method is "POST" or "PUT" or "PATCH" or "DELETE";
        var key = $"{binding.Provider.ToString().ToLowerInvariant()}:{Env(binding)}:{SellerScopeSegment(binding)}:bridge";
        return new IntegrationOutboundPolicy(
            isEffectful,
            [new IntegrationScope(key, TimeSpan.Zero, MaxConcurrency: 1)],
            binding.ResponseLimitBytes,
            HeaderProfile: IntegrationVendorHeaderProfile.Standard);
    }

    // ---------- Helpers ----------

    private static readonly IReadOnlyDictionary<string, IntegrationScopeSettlement> EmptySettlements =
        new Dictionary<string, IntegrationScopeSettlement>(StringComparer.Ordinal);

    private static IntegrationScope TokenBucket(string key, TimeSpan refillInterval, long burst) =>
        new(key, TimeSpan.Zero, MaxConcurrency: 0,
            WindowKind: IntegrationScopeWindowKind.TokenBucket,
            Burst: burst, RefillInterval: refillInterval, ReservedCost: 1);

    private static string Env(IntegrationHostRouteBinding binding) =>
        binding.Environment == IntegrationProviderEnvironment.Sandbox ? "sandbox" : "prod";

    private static string NormalizePath(string path) =>
        path.Length > 1 ? path.TrimEnd('/') : path;

    private static string NormalizeWbBucket(string path) =>
        WbObjectCharcsRegex().IsMatch(path) ? "/content/v2/object/charcs/{id}" : path;

    private static List<string> ExtractOzonStockPairs(IntegrationHostRouteBinding binding, string? body)
    {
        if (binding.EntityKeys is { Count: > 0 })
            return binding.EntityKeys.Distinct(StringComparer.Ordinal).ToList();

        var pairs = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(body))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("stocks", out var stocks) || stocks.ValueKind != JsonValueKind.Array)
                return [];
            foreach (var item in stocks.EnumerateArray())
            {
                var prod = TryReadId(item, "product_id") ?? TryReadId(item, "offer_id");
                var wh = TryReadId(item, "warehouse_id") ?? "default";
                if (prod is not null)
                    pairs.Add($"{prod}:{wh}");
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return pairs.ToList();
    }

    private static List<string> ExtractOzonPriceProducts(IntegrationHostRouteBinding binding, string? body)
    {
        if (binding.EntityKeys is { Count: > 0 })
            return binding.EntityKeys.Distinct(StringComparer.Ordinal).ToList();

        var products = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(body))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("prices", out var prices) || prices.ValueKind != JsonValueKind.Array)
                return [];
            foreach (var item in prices.EnumerateArray())
            {
                var prod = TryReadId(item, "product_id") ?? TryReadId(item, "offer_id");
                if (prod is not null)
                    products.Add(prod);
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return products.ToList();
    }

    private static string? TryReadId(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var val))
            return null;
        return val.ValueKind switch
        {
            JsonValueKind.Number => val.GetRawText(),
            JsonValueKind.String => string.IsNullOrWhiteSpace(val.GetString()) ? null : val.GetString()!.Trim(),
            _ => null
        };
    }

    private static long CountJsonArrayProperty(string? body, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(body))
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty(propertyName, out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.GetArrayLength();
        }
        catch (JsonException)
        {
            return 0;
        }
        return 0;
    }

    private static long CountYandexResponseElements(string body, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (path == "result.offers")
            {
                if (doc.RootElement.TryGetProperty("result", out var res) &&
                    res.TryGetProperty("offers", out var offers) && offers.ValueKind == JsonValueKind.Array)
                    return offers.GetArrayLength();
            }
            else if (path == "result.warehouses.offers")
            {
                if (doc.RootElement.TryGetProperty("result", out var res) &&
                    res.TryGetProperty("warehouses", out var warehouses) && warehouses.ValueKind == JsonValueKind.Array)
                {
                    long total = 0;
                    foreach (var wh in warehouses.EnumerateArray())
                    {
                        if (wh.TryGetProperty("offers", out var offers) && offers.ValueKind == JsonValueKind.Array)
                            total += offers.GetArrayLength();
                    }
                    return total;
                }
            }
        }
        catch (JsonException)
        {
            return -1;
        }
        return -1;
    }

    [GeneratedRegex("^/content/v2/object/charcs/[^/]+$")]
    private static partial Regex WbObjectCharcsRegex();

    [GeneratedRegex("^/api/v3/stocks/[^/]+$")]
    private static partial Regex WbStocksRegex();

    [GeneratedRegex("^/api/v3/orders/[^/]+/cancel$")]
    private static partial Regex WbOrderCancelRegex();

    [GeneratedRegex("^/api/v3/supplies/[^/]+$")]
    private static partial Regex WbSupplyIdRegex();

    [GeneratedRegex("^/api/v3/supplies/[^/]+/orders$")]
    private static partial Regex WbSupplyOrdersRegex();

    [GeneratedRegex("^/api/v3/supplies/[^/]+/order-ids$")]
    private static partial Regex WbSupplyOrderIdsRegex();

    [GeneratedRegex("^/api/v3/supplies/[^/]+/deliver$")]
    private static partial Regex WbSupplyDeliverRegex();

    [GeneratedRegex("^/api/v3/supplies/[^/]+/trbx$")]
    private static partial Regex WbSupplyTrbxRegex();

    [GeneratedRegex("^/api/v3/supplies/[^/]+/trbx/stickers$")]
    private static partial Regex WbSupplyTrbxStickersRegex();

    [GeneratedRegex("^/api/v3/supplies/[^/]+/trbx/[^/]+$")]
    private static partial Regex WbSupplyTrbxBoxRegex();

    [GeneratedRegex("^/api/v3/supplies/[^/]+/barcode$")]
    private static partial Regex WbSupplyBarcodeRegex();

    [GeneratedRegex("^/v2/campaigns/([^/]+)/")]
    private static partial Regex YandexCampaignIdRegex();

    [GeneratedRegex("^/v2/businesses/([^/]+)/")]
    private static partial Regex YandexBusinessIdRegex();

    [GeneratedRegex("^/v2/category/[^/]+/parameters$")]
    private static partial Regex YandexCategoryParamsRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/offer-mappings$")]
    private static partial Regex YandexBizOfferMappingsRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/offer-mappings/update$")]
    private static partial Regex YandexBizOfferMappingsUpdateRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/offer-cards/update$")]
    private static partial Regex YandexBizOfferCardsUpdateRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/offer-cards$")]
    private static partial Regex YandexBizOfferCardsRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/offer-mappings/archive$")]
    private static partial Regex YandexBizOfferMappingsArchiveRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/offer-prices$")]
    private static partial Regex YandexBizOfferPricesRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/offer-prices/updates$")]
    private static partial Regex YandexBizOfferPricesUpdatesRegex();

    [GeneratedRegex("^/v2/campaigns/[^/]+/offers/stocks$")]
    private static partial Regex YandexCampaignStocksRegex();

    [GeneratedRegex("^/v2/campaigns/[^/]+/orders$")]
    private static partial Regex YandexCampaignOrdersRegex();

    [GeneratedRegex("^/v2/campaigns/[^/]+/orders/[^/]+$")]
    private static partial Regex YandexCampaignOrderDetailRegex();

    [GeneratedRegex("^/v2/campaigns/[^/]+/orders/[^/]+/status$")]
    private static partial Regex YandexCampaignOrderStatusRegex();

    [GeneratedRegex("^/v2/campaigns/[^/]+/orders/[^/]+/delivery/labels$")]
    private static partial Regex YandexCampaignOrderLabelsRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/promos$")]
    private static partial Regex YandexBizPromosRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/promos/offers/update$")]
    private static partial Regex YandexBizPromosUpdateRegex();

    [GeneratedRegex("^/v2/businesses/[^/]+/promos/offers/delete$")]
    private static partial Regex YandexBizPromosDeleteRegex();

    [GeneratedRegex("^/core/v1/items/[^/]+/update_price$")]
    private static partial Regex AvitoUpdatePriceRegex();
}
