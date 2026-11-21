using System.Net;
using System.Net.Http.Headers;
using Delegat.Marketplace.SDK;
using Delegat.Models.Integration;
using Delegat.Services.Integration;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public sealed class IntegrationExecutionRoutePolicyTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 22, 30, 0, TimeSpan.Zero); // 01:30 MSK
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }

    private static ServiceProvider BuildProvider(
        Func<DataContext> createContext,
        TimeProvider clock,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => createContext())
            .AddSingleton(clock)
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(send));
        return services.BuildServiceProvider();
    }

    private static async Task<IntegrationTransportAttempt> SeedAttemptAsync(
        DataContext db, DateTimeOffset now, string? payload = null, bool isEffectful = true)
    {
        var stage = new IntegrationStage
        {
            command = new IntegrationCommand
            {
                commandId = $"cmd-{Guid.NewGuid():N}",
                operationId = $"op-{Guid.NewGuid():N}",
                kind = "export-product",
                requestHash = IntegrationCoordinator.ComputeHash("{}"),
                scopeKey = $"scope-{Guid.NewGuid():N}",
                inputJson = "{}"
            },
            itemKey = "item-1",
            stageKey = $"stage-{Guid.NewGuid():N}",
            kind = "create",
            state = IntegrationStageState.InFlight,
            isEffectful = isEffectful,
            claimVersion = 2,
            claimedBy = "worker",
            claimedUntil = now.AddMinutes(5).UtcDateTime,
            attemptCount = 1,
            payloadJson = payload
        };
        db.integrationStages.Add(stage);
        await db.SaveChangesAsync();
        var attempt = new IntegrationAttempt
        {
            commandId = stage.commandId,
            stageId = stage.Id,
            stageKey = stage.stageKey,
            dispatchId = $"disp-{Guid.NewGuid():N}",
            attemptNumber = 1,
            requestKey = $"req-{Guid.NewGuid():N}",
            requestHash = IntegrationCoordinator.ComputeHash(payload ?? "")
        };
        db.integrationAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return new(stage.commandId, stage.Id, attempt.Id, stage.claimVersion, isEffectful, now.AddMinutes(5));
    }

    [Fact]
    public async Task SameSellerUnderTwoKeys_SharesBudgetAndIgnoresSdkHints()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);

        // Two distinct connections/keys bound to the same verified WB seller with Base token class.
        var wbOrigin = new Uri("https://content-api.wildberries.ru");
        var key1Binding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.Wildberries, IntegrationProviderEnvironment.Production, wbOrigin,
            VerifiedSellerId: "wb-seller-100", WbTokenClass: WildberriesTokenClass.Base);
        var key2Binding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.Wildberries, IntegrationProviderEnvironment.Production, wbOrigin,
            VerifiedSellerId: "wb-seller-100", WbTokenClass: WildberriesTokenClass.Base);

        var calls = 0;
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock, (_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        });
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();

        // Base token class for /content/v2/directory/colors: burst 1, 30-minute refill (2/hour).
        var a1 = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
        var req1 = new IntegrationTransportRequest("GET", new Uri(wbOrigin, "/content/v2/directory/colors"),
            new Dictionary<string, string> { ["Authorization"] = "key-alpha" }, null,
            new IntegrationRequestPolicy("sdk-hint-conn-1", IsEffectful: false, RequestCost: 999m));
        var r1 = await transport.Bind(a1, r => IntegrationRoutePolicyCatalog.Resolve(key1Binding, r)).SendAsync(req1);
        Assert.Equal(200, r1.StatusCode);

        // Second key for the same seller hits the exact same budget and is deferred for 30 minutes.
        var a2 = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
        var req2 = new IntegrationTransportRequest("GET", new Uri(wbOrigin, "/content/v2/directory/colors"),
            new Dictionary<string, string> { ["Authorization"] = "key-beta" }, null,
            new IntegrationRequestPolicy("sdk-hint-conn-2", IsEffectful: false, RequestCost: 0.01m));
        var ex = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            transport.Bind(a2, r => IntegrationRoutePolicyCatalog.Resolve(key2Binding, r)).SendAsync(req2));
        Assert.Equal("transport_admission_deferred", ex.Code);
        Assert.Equal(clock.Now.AddMinutes(30), ex.RetryAt);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnknownRoutesOffOriginAndUnmatchedWbShipmentRoutes_FailClosed()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);

        var calls = 0;
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock, (_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();

        var wbBinding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.Wildberries, IntegrationProviderEnvironment.Production,
            new Uri("https://marketplace-api.wildberries.ru"), VerifiedSellerId: "wb-1",
            WbTokenClass: WildberriesTokenClass.Personal);

        var badRequests = new[]
        {
            // Unmatched WB shipment route from audit ledger (line 968).
            new IntegrationTransportRequest("PATCH", new Uri("https://marketplace-api.wildberries.ru/api/v3/supplies/WB-GI-1/orders"),
                new Dictionary<string, string>(), "{}", new IntegrationRequestPolicy("hint", IsEffectful: true)),
            // Off-origin host.
            new IntegrationTransportRequest("GET", new Uri("https://evil.example/api/v3/warehouses"),
                new Dictionary<string, string>(), null, new IntegrationRequestPolicy("hint", IsEffectful: false)),
            // Unknown route.
            new IntegrationTransportRequest("GET", new Uri("https://marketplace-api.wildberries.ru/api/v3/unknown-endpoint"),
                new Dictionary<string, string>(), null, new IntegrationRequestPolicy("hint", IsEffectful: false))
        };

        foreach (var req in badRequests)
        {
            var attempt = await SeedAttemptAsync(db, clock.Now, payload: req.Body, isEffectful: req.Policy.IsEffectful);
            var ex = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
                transport.Bind(attempt, r => IntegrationRoutePolicyCatalog.Resolve(wbBinding, r)).SendAsync(req));
            Assert.Equal("transport_policy_missing_or_mismatched", ex.Code);
        }
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Wildberries_Weighted4xxErrorCostAndLongXRatelimitRetryArePreserved()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);

        var wbOrigin = new Uri("https://marketplace-api.wildberries.ru");
        var binding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.Wildberries, IntegrationProviderEnvironment.Production, wbOrigin,
            VerifiedSellerId: "wb-seller-fbs", WbTokenClass: WildberriesTokenClass.Personal);

        var responseIndex = 0;
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock, (_, _) =>
        {
            responseIndex++;
            if (responseIndex <= 2)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") });
            var throttled = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("slow down") };
            throttled.Headers.TryAddWithoutValidation("X-Ratelimit-Retry", "7200"); // 2 hours, must NOT be clamped to 60s!
            return Task.FromResult(throttled);
        });
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();

        // FBS burst is 20; each 4XX costs 10 requests -> two 400s exhaust the burst of 20!
        for (var i = 0; i < 2; i++)
        {
            var a = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
            var req = new IntegrationTransportRequest("GET", new Uri(wbOrigin, "/api/v3/warehouses"),
                new Dictionary<string, string>(), null, new IntegrationRequestPolicy("hint", IsEffectful: false));
            var resp = await transport.Bind(a, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(req);
            Assert.Equal(400, resp.StatusCode);
        }

        // 3rd call immediately is blocked for 200ms (1 token refill).
        var a3 = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
        var req3 = new IntegrationTransportRequest("GET", new Uri(wbOrigin, "/api/v3/warehouses"),
            new Dictionary<string, string>(), null, new IntegrationRequestPolicy("hint", IsEffectful: false));
        var burstEx = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            transport.Bind(a3, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(req3));
        Assert.Equal("transport_admission_deferred", burstEx.Code);
        Assert.Equal(clock.Now.AddMilliseconds(200), burstEx.RetryAt);

        // Advance 200ms -> 3rd call goes through and receives 429 with X-Ratelimit-Retry: 7200 (2 hours).
        clock.Now = clock.Now.AddMilliseconds(200);
        var a4 = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
        var r4 = await transport.Bind(a4, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(req3);
        Assert.Equal(429, r4.StatusCode);

        // Next call after simulated restart (new provider) must observe full 7200s cooldown.
        await using var restarted = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock,
            (_, _) => throw new InvalidOperationException("Must not send HTTP during 2-hour WB cooldown."));
        var a5 = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
        var longEx = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            restarted.GetRequiredService<IntegrationHttpTransport>()
                .Bind(a5, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(req3));
        Assert.Equal("transport_admission_deferred", longEx.Code);
        Assert.Equal(clock.Now.AddSeconds(7200), longEx.RetryAt);
    }

    [Fact]
    public async Task Ozon_EntitySpacingDynamicDailyMoscowResetAndItemRetryMinutes()
    {
        var clock = new ManualClock(); // 2026-11-20 22:30 UTC == 2026-11-21 01:30 MSK (1.5h before 03:00 MSK reset)
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);

        var ozonOrigin = new Uri("https://api-seller.ozon.ru");
        var binding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.Ozon, IntegrationProviderEnvironment.Production, ozonOrigin,
            VerifiedSellerId: "ozon-client-77");

        var mode = "ok";
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock, (_, _) =>
        {
            if (mode == "item-429")
            {
                var resp = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("{}") };
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                resp.Headers.TryAddWithoutValidation("Item-Retry-After", "15"); // 15 MINUTES, not 15 seconds!
                return Task.FromResult(resp);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        });
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();

        // 1) Stock pair (product 10, warehouse 5) enforces 30s entity spacing; different pair is isolated!
        const string stockPairA = """{"stocks":[{"product_id":10,"warehouse_id":5,"stock":3}]}""";
        const string stockPairB = """{"stocks":[{"product_id":10,"warehouse_id":6,"stock":3}]}""";
        var s1 = await SeedAttemptAsync(db, clock.Now, payload: stockPairA, isEffectful: true);
        var stockReqA = new IntegrationTransportRequest("POST", new Uri(ozonOrigin, "/v2/products/stocks"),
            new Dictionary<string, string>(), stockPairA, new IntegrationRequestPolicy("h", IsEffectful: true));
        Assert.Equal(200, (await transport.Bind(s1, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(stockReqA)).StatusCode);

        // Immediate repeat for same pair -> deferred 30s.
        var s2 = await SeedAttemptAsync(db, clock.Now, payload: stockPairA, isEffectful: true);
        var pairEx = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            transport.Bind(s2, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(stockReqA));
        Assert.Equal("transport_admission_deferred", pairEx.Code);
        Assert.Equal(clock.Now.AddSeconds(30), pairEx.RetryAt);

        // Different warehouse pair (10:6) succeeds immediately!
        var s3 = await SeedAttemptAsync(db, clock.Now, payload: stockPairB, isEffectful: true);
        var stockReqB = new IntegrationTransportRequest("POST", new Uri(ozonOrigin, "/v2/products/stocks"),
            new Dictionary<string, string>(), stockPairB, new IntegrationRequestPolicy("h", IsEffectful: true));
        Assert.Equal(200, (await transport.Bind(s3, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(stockReqB)).StatusCode);

        // 2) Dynamic daily item quota (e.g. observed 2 items/day from /v4/product/info/limit) resets at 03:00 MSK (00:00 UTC).
        await admission.RecordObservedQuotaAsync(
            IntegrationRoutePolicyCatalog.OzonDailyImportScopeKey(binding),
            observedLimit: 2, observedRemaining: 2,
            resetAt: new DateTimeOffset(IntegrationScopeAdmission.ComputeNextCalendarResetTicks(
                clock.Now, IntegrationRoutePolicyCatalog.OzonResetTimeZoneId, IntegrationRoutePolicyCatalog.OzonResetHourOfDay), TimeSpan.Zero),
            CancellationToken.None);

        const string import2Items = """{"items":[{"offer_id":"a"},{"offer_id":"b"}]}""";
        var imp1 = await SeedAttemptAsync(db, clock.Now, payload: import2Items, isEffectful: true);
        var impReq = new IntegrationTransportRequest("POST", new Uri(ozonOrigin, "/v3/product/import"),
            new Dictionary<string, string>(), import2Items, new IntegrationRequestPolicy("h", IsEffectful: true));
        Assert.Equal(200, (await transport.Bind(imp1, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(impReq)).StatusCode);

        // Next import exceeds daily limit of 2 -> deferred until 2026-11-21 00:00:00 UTC (03:00 MSK)!
        var imp2 = await SeedAttemptAsync(db, clock.Now, payload: import2Items, isEffectful: true);
        var dailyEx = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            transport.Bind(imp2, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(impReq));
        Assert.Equal("transport_admission_deferred", dailyEx.Code);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero), dailyEx.RetryAt);

        // 3) Item-Retry-After: 15 (MINUTES) beats Retry-After: 30 (seconds).
        clock.Now = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero); // Reset passed!
        mode = "item-429";
        var imp3 = await SeedAttemptAsync(db, clock.Now, payload: import2Items, isEffectful: true);
        Assert.Equal(429, (await transport.Bind(imp3, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(impReq)).StatusCode);

        var imp4 = await SeedAttemptAsync(db, clock.Now, payload: import2Items, isEffectful: true);
        var minutesEx = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            transport.Bind(imp4, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(impReq));
        Assert.Equal("transport_admission_deferred", minutesEx.Code);
        Assert.Equal(clock.Now.AddMinutes(15), minutesEx.RetryAt);
    }

    [Fact]
    public async Task YandexMarket_Conservative4Parallel512KbCapResponseSettlement5xxRefundAnd420Until()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);

        var ymOrigin = new Uri("https://api.partner.market.yandex.ru");
        var binding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.YandexMarket, IntegrationProviderEnvironment.Production, ymOrigin,
            VerifiedSellerId: "ym-biz-55", YandexTier: YandexResourceTier.None, ExpectedMaxResponseItems: 100);

        var nextResponse = (Func<HttpResponseMessage>)(() =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"result":{"offers":[{"offerId":"1"},{"offerId":"2"},{"offerId":"3"}]}}""")
            });

        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock,
            (_, _) => Task.FromResult(nextResponse()));
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();

        // 1) Body > 512 KB blocked before HTTP.
        var hugeBody = "{\"offers\":[\"" + new string('a', IntegrationRoutePolicyCatalog.YandexMaxBodyBytes) + "\"]}";
        var bigAttempt = await SeedAttemptAsync(db, clock.Now, payload: hugeBody, isEffectful: true);
        var bigReq = new IntegrationTransportRequest("POST", new Uri(ymOrigin, "/v2/businesses/55/offer-prices/updates"),
            new Dictionary<string, string>(), hugeBody, new IntegrationRequestPolicy("h", IsEffectful: true));
        var bigEx = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            transport.Bind(bigAttempt, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(bigReq));
        Assert.Equal("transport_request_body_too_large", bigEx.Code);

        // 2) Conservative 4-parallel concurrency: 4 active leases succeed, 5th is deferred!
        var concScope = new IntegrationScope("yandex:prod:seller:ym-biz-55:conc:campaign:99", TimeSpan.Zero, MaxConcurrency: 4);
        var leases = new List<IntegrationScopeLease>();
        for (var i = 0; i < 4; i++)
        {
            var r = await admission.TryReserveAsync([concScope], TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.NotNull(r.Lease);
            leases.Add(r.Lease!);
        }
        var fifth = await admission.TryReserveAsync([concScope], TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Null(fifth.Lease);
        Assert.Equal(clock.Now.AddMinutes(1), fifth.RetryAt);
        foreach (var l in leases)
            await admission.CompleteAsync(l, null, CancellationToken.None);

        // 3) Response-sized settlement: reserves upper bound 100, settles to actual 3 offers from response JSON!
        var priceReadAttempt = await SeedAttemptAsync(db, clock.Now, payload: "{}", isEffectful: false);
        var priceReadReq = new IntegrationTransportRequest("POST", new Uri(ymOrigin, "/v2/businesses/55/offer-prices"),
            new Dictionary<string, string>(), "{}", new IntegrationRequestPolicy("h", IsEffectful: false));
        Assert.Equal(200, (await transport.Bind(priceReadAttempt, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(priceReadReq)).StatusCode);

        var resKey = IntegrationScopeAdmission.HashScopeKey("yandex:prod:seller:ym-biz-55:res:offer-prices-read");
        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var row = await verify.integrationScopeStates.AsNoTracking().SingleAsync(s => s.scopeKey == resKey);
            Assert.Equal(3, row.usedUnits);
        }

        // 4) 5xx refunds resource cost to 0 (usedUnits stays 3, not 103!).
        nextResponse = () => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("err") };
        var errAttempt = await SeedAttemptAsync(db, clock.Now, payload: "{}", isEffectful: false);
        Assert.Equal(500, (await transport.Bind(errAttempt, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(priceReadReq)).StatusCode);
        await using (var verify = TestDbFactory.CreateSqliteContext(dsn))
        {
            var row = await verify.integrationScopeStates.AsNoTracking().SingleAsync(s => s.scopeKey == resKey);
            Assert.Equal(3, row.usedUnits);
        }

        // 5) 420 + X-RateLimit-Resource-Until (RFC822 date) persists exact cooldown!
        var until = clock.Now.AddMinutes(12);
        nextResponse = () =>
        {
            var resp = new HttpResponseMessage((HttpStatusCode)420) { Content = new StringContent("limit") };
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Resource-Until", until.ToString("R"));
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Resource-Limit", "5000");
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Resource-Remaining", "0");
            return resp;
        };
        var limAttempt = await SeedAttemptAsync(db, clock.Now, payload: "{}", isEffectful: false);
        Assert.Equal(420, (await transport.Bind(limAttempt, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(priceReadReq)).StatusCode);

        var nextAttempt = await SeedAttemptAsync(db, clock.Now, payload: "{}", isEffectful: false);
        var ymEx = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            transport.Bind(nextAttempt, r => IntegrationRoutePolicyCatalog.Resolve(binding, r)).SendAsync(priceReadReq));
        Assert.Equal("transport_admission_deferred", ymEx.Code);
        Assert.Equal(until, ymEx.RetryAt);
    }

    [Fact]
    public async Task AvitoAuthAndNormalRoutes_AndDelegatBridges_AreExplicitlyAdmitted()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);

        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock, (_, _) =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "25");
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            return Task.FromResult(resp);
        });
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();

        // Avito /token auth AND /core/v1/items are both admitted.
        var avitoOrigin = new Uri("https://api.avito.ru");
        var avitoBinding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.Avito, IntegrationProviderEnvironment.Production, avitoOrigin,
            VerifiedSellerId: "avito-user-9");

        var authAttempt = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
        var authReq = new IntegrationTransportRequest("POST", new Uri(avitoOrigin, "/token"),
            new Dictionary<string, string>(), null, new IntegrationRequestPolicy("h", IsEffectful: false));
        Assert.Equal(200, (await transport.Bind(authAttempt, r => IntegrationRoutePolicyCatalog.Resolve(avitoBinding, r)).SendAsync(authReq)).StatusCode);

        // /core/v1/items observes X-RateLimit-Remaining: 0 -> next call blocked for 1 minute!
        var listAttempt1 = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
        var listReq = new IntegrationTransportRequest("GET", new Uri(avitoOrigin, "/core/v1/items"),
            new Dictionary<string, string>(), null, new IntegrationRequestPolicy("h", IsEffectful: false));
        Assert.Equal(200, (await transport.Bind(listAttempt1, r => IntegrationRoutePolicyCatalog.Resolve(avitoBinding, r)).SendAsync(listReq)).StatusCode);

        var listAttempt2 = await SeedAttemptAsync(db, clock.Now, isEffectful: false);
        var avitoEx = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            transport.Bind(listAttempt2, r => IntegrationRoutePolicyCatalog.Resolve(avitoBinding, r)).SendAsync(listReq));
        Assert.Equal("transport_admission_deferred", avitoEx.Code);
        Assert.Equal(clock.Now.AddMinutes(1), avitoEx.RetryAt);

        // Delegat bridges (OpenCart, WooCommerce, Medusa): protective serialization only, off-bridge paths fail closed.
        var wooOrigin = new Uri("https://shop.example");
        var wooBinding = new IntegrationHostRouteBinding(
            IntegrationProviderKind.WooCommerce, IntegrationProviderEnvironment.Production, wooOrigin,
            VerifiedSellerId: "shop-1");
        var nativeStoreApi = new IntegrationTransportRequest("GET", new Uri(wooOrigin, "/wp-json/wc/store/v1/products"),
            new Dictionary<string, string>(), null, new IntegrationRequestPolicy("h", IsEffectful: false));
        Assert.Null(IntegrationRoutePolicyCatalog.Resolve(wooBinding, nativeStoreApi));

        var bridgePolicy = IntegrationRoutePolicyCatalog.Resolve(wooBinding,
            new IntegrationTransportRequest("POST", new Uri(wooOrigin, "/wp-json/delegat-bridge/v1/products"),
                new Dictionary<string, string>(), "{}", new IntegrationRequestPolicy("h", IsEffectful: true)));
        Assert.NotNull(bridgePolicy);
        Assert.True(bridgePolicy!.IsEffectful);
        var bridgeScope = Assert.Single(bridgePolicy.Scopes);
        Assert.Equal(1, bridgeScope.MaxConcurrency);
        Assert.Equal(IntegrationScopeWindowKind.None, bridgeScope.WindowKind);
    }
}
