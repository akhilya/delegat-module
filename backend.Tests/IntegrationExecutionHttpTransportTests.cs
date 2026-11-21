using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using Delegat.Marketplace.SDK;
using Delegat.Models.Integration;
using Delegat.Services.Integration;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Tests;

public sealed class IntegrationExecutionHttpTransportTests
{
    private const string Payload = """{"sku":"test"}""";
    private static readonly IntegrationScope SharedScope = new("fixture-provider-seller", TimeSpan.Zero);

    private sealed class ManualClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }

    private static ServiceProvider BuildProvider(Func<DataContext> createContext, TimeProvider clock,
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

    private static async Task<IntegrationTransportAttempt> SeedAsync(DataContext db, DateTimeOffset now,
        bool stopped = false)
    {
        var stage = new IntegrationStage
        {
            command = new IntegrationCommand
            {
                commandId = "command-transport", operationId = "operation-transport", kind = "export-product",
                requestHash = IntegrationCoordinator.ComputeHash("{}"), scopeKey = "scope", inputJson = "{}",
                stopRequested = stopped
            },
            itemKey = "item", stageKey = "create", kind = "create", state = IntegrationStageState.InFlight,
            claimVersion = 2, claimedBy = "worker", claimedUntil = now.AddMinutes(2).UtcDateTime,
            attemptCount = 1, payloadJson = Payload
        };
        db.integrationStages.Add(stage);
        await db.SaveChangesAsync();
        var attempt = new IntegrationAttempt
        {
            commandId = stage.commandId, stageId = stage.Id, stageKey = stage.stageKey, dispatchId = "dispatch",
            attemptNumber = 1, requestKey = "request", requestHash = IntegrationCoordinator.ComputeHash(Payload)
        };
        db.integrationAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return new(stage.commandId, stage.Id, attempt.Id, stage.claimVersion, true, now.AddMinutes(2));
    }

    private static IntegrationTransportRequest Request(bool effectful = true, string scopeHint = "untrusted-hint") =>
        new(effectful ? "POST" : "GET", new Uri("https://provider.test/items"),
            new Dictionary<string, string> { ["Authorization"] = "Bearer fixture" }, effectful ? Payload : null,
            new IntegrationRequestPolicy(scopeHint, IsEffectful: effectful));

    private static IntegrationOutboundPolicy? Resolve(IntegrationTransportRequest request) =>
        request.Url == new Uri("https://provider.test/items") && request.Method is "POST" or "GET"
            ? new(request.Method == "POST", [SharedScope], 1024)
            : null;

    [Fact]
    public async Task EffectfulSend_CommitsFenceAndCannotSendTwice()
    {
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        await AssertSingleEffectfulSendAsync(db, () => TestDbFactory.CreateSqliteContext(dsn));
    }

    [LocalMySqlFact]
    public async Task EffectfulSend_CommitsFenceOnMySql()
    {
        await using var mysql = await LocalMySqlDatabase.CreateAsync();
        await using var db = mysql.CreateContext();
        await AssertSingleEffectfulSendAsync(db, () => mysql.CreateContext());
    }

    private static async Task AssertSingleEffectfulSendAsync(DataContext db, Func<DataContext> createContext)
    {
        var clock = new ManualClock();
        var attempt = await SeedAsync(db, clock.GetUtcNow());
        var now = clock.GetUtcNow();

        var calls = 0;
        await using var provider = BuildProvider(createContext, clock, async (req, ct) =>
        {
            calls++;
            Assert.Equal(new Uri("https://provider.test/items"), req.RequestUri);
            Assert.Equal("Bearer fixture", req.Headers.Authorization?.ToString());
            Assert.Equal(Payload, await req.Content!.ReadAsStringAsync(ct));

            // The durable fence is committed and the scope lease is owned before the fake reply.
            await using var verify = createContext();
            var attemptRow = await verify.integrationAttempts.AsNoTracking().SingleAsync(ct);
            Assert.Equal(now.UtcDateTime, attemptRow.sendAuthorizedAt);
            var scopeRow = await verify.integrationScopeStates.AsNoTracking().SingleAsync(ct);
            Assert.NotNull(scopeRow.leaseOwner);

            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("accepted")
            };
        });
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();
        var bound = transport.Bind(attempt, Resolve);

        var result = await bound.SendAsync(Request());

        // Accepted is a response, not proof the provider applied the effect.
        Assert.Equal(202, result.StatusCode);
        Assert.Equal("accepted", result.Body);

        var ex = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            bound.SendAsync(Request()));
        Assert.Equal("transport_attempt_or_scope_not_current", ex.Code);
        Assert.Equal(1, calls);

        await using var final = createContext();
        Assert.Null((await final.integrationScopeStates.AsNoTracking().SingleAsync()).leaseOwner);
    }

    [Theory]
    [InlineData("missing-attempt")]
    [InlineData("stale-claim")]
    [InlineData("expired-claim")]
    [InlineData("payload-mismatch")]
    [InlineData("stopped")]
    [InlineData("confirmation")]
    [InlineData("missing-policy")]
    [InlineData("effect-mismatch")]
    public async Task EffectfulSend_BlockedBeforeHttp(string reason)
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        var attempt = await SeedAsync(db, clock.GetUtcNow(), stopped: reason == "stopped");
        if (reason == "expired-claim")
        {
            // Expire the committed claim but keep the bound token: the final fence SQL must catch it.
            await db.integrationStages.Where(s => s.Id == attempt.StageId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.claimedUntil, clock.GetUtcNow().UtcDateTime));
        }

        var calls = 0;
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock, (req, ct) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();

        var boundAttempt = reason switch
        {
            "missing-attempt" => attempt with { AttemptId = attempt.AttemptId + 1 },
            "stale-claim" => attempt with { ClaimVersion = attempt.ClaimVersion - 1 },
            "confirmation" => attempt with { AllowEffects = false },
            _ => attempt
        };
        var request = reason switch
        {
            "payload-mismatch" => Request() with { Body = "{}" },
            "missing-policy" => Request() with { Url = new Uri("https://unbound.test/items") },
            "effect-mismatch" => Request() with { Policy = Request().Policy with { IsEffectful = false } },
            _ => Request()
        };
        var bound = transport.Bind(boundAttempt, Resolve);

        var ex = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            bound.SendAsync(request));

        var expectedCode = reason switch
        {
            "missing-policy" or "effect-mismatch" => "transport_policy_missing_or_mismatched",
            "confirmation" => "transport_effect_not_permitted",
            _ => "transport_attempt_or_scope_not_current"
        };
        Assert.Equal(expectedCode, ex.Code);
        Assert.Equal(0, calls);
        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Null((await verify.integrationAttempts.AsNoTracking().SingleAsync()).sendAuthorizedAt);
    }

    [Fact]
    public async Task EffectfulSend_RechecksCooldownAfterScopeReservation()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        await using var previousProvider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock,
            (_, _) => throw new InvalidOperationException("The previous owner must not send HTTP."));
        var admission = previousProvider.GetRequiredService<IntegrationScopeAdmission>();
        var previous = await admission.TryReserveAsync([SharedScope], TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.NotNull(previous.Lease);
        clock.Now = previous.Lease.ExpiresAt;
        var attempt = await SeedAsync(db, clock.Now);
        var cooldown = clock.Now.AddHours(1);
        var interceptor = new BeforeSendFence(ct => admission.CompleteAsync(previous.Lease, cooldown, ct));

        var calls = 0;
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn, interceptors: [interceptor]),
            clock, (_, _) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });
        var bound = provider.GetRequiredService<IntegrationHttpTransport>().Bind(attempt, Resolve);
        var blocked = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() => bound.SendAsync(Request()));
        Assert.Equal("transport_attempt_or_scope_not_current", blocked.Code);
        Assert.Equal(0, calls);
        Assert.Null((await db.integrationAttempts.AsNoTracking().SingleAsync()).sendAuthorizedAt);
        var next = await admission.TryReserveAsync([SharedScope], TimeSpan.FromMinutes(2), CancellationToken.None);
        Assert.Null(next.Lease);
        Assert.Equal(cooldown, next.RetryAt);
    }

    private sealed class BeforeSendFence(Func<CancellationToken, Task> observeCooldown) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"integrationAttempts\"", StringComparison.Ordinal))
                await observeCooldown(cancellationToken);
            return result;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoppedConfirmation_UsesSharedCooldownEvenWhenBodyUnavailable(bool oversized)
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        var attempt = (await SeedAsync(db, clock.GetUtcNow(), stopped: true)) with { AllowEffects = false };

        var calls = 0;
        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock, (req, ct) =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent(oversized ? new string('x', 1025) : "limited")
            };
            response.Headers.RetryAfter = oversized
                ? new RetryConditionHeaderValue(clock.GetUtcNow().AddHours(5))
                : new RetryConditionHeaderValue(TimeSpan.FromHours(5));
            return Task.FromResult(response);
        });
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();
        var bound = transport.Bind(attempt, Resolve);

        var first = await bound.SendAsync(Request(effectful: false));
        Assert.Equal(oversized ? 0 : 429, first.StatusCode);

        var ex = await Assert.ThrowsAsync<IntegrationTransportBlockedException>(() =>
            bound.SendAsync(Request(effectful: false, scopeHint: "another-hint")));
        Assert.Equal("transport_admission_deferred", ex.Code);
        Assert.Equal(clock.GetUtcNow().AddHours(5), ex.RetryAt);
        Assert.Equal(1, calls);

        await using var verify = TestDbFactory.CreateSqliteContext(dsn);
        Assert.Null((await verify.integrationAttempts.AsNoTracking().SingleAsync()).sendAuthorizedAt);
    }

    private sealed class RecordingHttpLogger(bool throwOnLog = false) : IAdapterHttpLogger
    {
        public List<AdapterHttpLogEntry> Entries { get; } = [];
        public void Log(AdapterHttpLogEntry entry)
        {
            if (throwOnLog)
                throw new InvalidOperationException("simulated logger failure");
            Entries.Add(entry);
        }
    }

    [Fact]
    public async Task V1Transport_LogsCorrelatedSanitizedExchange_WithoutSecretsOrRawBodies()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        var attempt = (await SeedAsync(db, clock.GetUtcNow())) with
        {
            OperationId = "op-corr-42",
            StoreId = 3,
            ConnectionId = 19,
            AdapterKey = "ozon"
        };

        var logger = new RecordingHttpLogger();
        var services = new ServiceCollection()
            .AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IAdapterHttpLogger>(logger)
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"secret":"top-secret"}""") };
                resp.Headers.TryAddWithoutValidation("Set-Cookie", "sid=super-secret-cookie");
                resp.Headers.TryAddWithoutValidation("X-Request-Id", "req-trace-1");
                return Task.FromResult(resp);
            }));
        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();

        var req = new IntegrationTransportRequest("POST", new Uri("https://provider.test/items?api_key=leak-me"),
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer super-secret-token",
                ["X-Delegat-Api-Key"] = "bridge-secret-key",
                ["X-Safe-Header"] = "visible-value"
            },
            Payload,
            new IntegrationRequestPolicy("hint", IsEffectful: true));

        var result = await transport.Bind(attempt, r =>
            r.Url.AbsolutePath == "/items" ? new(true, [SharedScope], 1024) : null).SendAsync(req);

        Assert.Equal(200, result.StatusCode);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("op-corr-42", entry.OperationId);
        Assert.Equal(3, entry.StoreId);
        Assert.Equal(19, entry.ConnectionId);
        Assert.Equal("ozon", entry.AdapterKey);
        Assert.Equal("https://provider.test/items", entry.Url);
        Assert.DoesNotContain("leak-me", entry.Url);
        Assert.DoesNotContain("super-secret-token", entry.RequestHeadersJson);
        Assert.DoesNotContain("bridge-secret-key", entry.RequestHeadersJson);
        Assert.Contains("visible-value", entry.RequestHeadersJson);
        Assert.DoesNotContain("super-secret-cookie", entry.ResponseHeadersJson);
        Assert.Contains("req-trace-1", entry.ResponseHeadersJson);
        Assert.Null(entry.RequestBody);
        Assert.Null(entry.ResponseBody);

        // Durable scope snapshot is observable without leaking raw credentials.
        var snapshots = await admission.GetSnapshotsAsync([SharedScope.Key], CancellationToken.None);
        var snap = Assert.Single(snapshots);
        Assert.Equal(SharedScope.Key, snap.RawKey);
        Assert.Equal(0, snap.ActiveLeases);
    }

    [Fact]
    public async Task V1Transport_ThrowingLoggerNeverDropsResponseOrCooldown()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        var attempt = (await SeedAsync(db, clock.GetUtcNow())) with { AllowEffects = false };

        var services = new ServiceCollection()
            .AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IAdapterHttpLogger>(new RecordingHttpLogger(throwOnLog: true))
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
            {
                var resp = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("slow") };
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
                return Task.FromResult(resp);
            }));
        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();

        var result = await transport.Bind(attempt, Resolve).SendAsync(Request(effectful: false));
        Assert.Equal(429, result.StatusCode);

        var snapshots = await admission.GetSnapshotsAsync([SharedScope.Key], CancellationToken.None);
        Assert.Equal(clock.GetUtcNow().AddMinutes(2), Assert.Single(snapshots).CooldownUntil);
    }

    [Fact]
    public async Task V1Transport_Oversized200Body_LogsZeroStatusAndTransportIoError()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);
        var attempt = (await SeedAsync(db, clock.GetUtcNow())) with { AllowEffects = false };

        var logger = new RecordingHttpLogger();
        var services = new ServiceCollection()
            .AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IAdapterHttpLogger>(logger)
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new string('x', 1025))
                })));
        await using var provider = services.BuildServiceProvider();
        var transport = provider.GetRequiredService<IntegrationHttpTransport>();

        var result = await transport.Bind(attempt, Resolve).SendAsync(Request(effectful: false));
        Assert.Equal(0, result.StatusCode);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(0, entry.StatusCode);
        Assert.Equal("transport_io_error", entry.ErrorMessage);
    }

    [Fact]
    public async Task ScopeSnapshots_ClearExpiredWindowUsageAndObservations()
    {
        var clock = new ManualClock();
        var (db, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = db;
        var dsn = TestDbFactory.SqliteDsn(db);

        await using var provider = BuildProvider(() => TestDbFactory.CreateSqliteContext(dsn), clock,
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var admission = provider.GetRequiredService<IntegrationScopeAdmission>();

        var windowed = new IntegrationScope("windowed-scope", TimeSpan.Zero, MaxConcurrency: 0,
            WindowKind: IntegrationScopeWindowKind.FixedWindow, Allowance: 10, Period: TimeSpan.FromMinutes(1));
        var lease = (await admission.TryReserveAsync([windowed], TimeSpan.FromSeconds(30), CancellationToken.None)).Lease;
        Assert.NotNull(lease);
        await admission.CompleteAsync(lease!, new Dictionary<string, IntegrationScopeSettlement>
        {
            [windowed.Key] = new(ObservedLimit: 10, ObservedRemaining: 9)
        }, null, CancellationToken.None);

        var activeSnap = Assert.Single(await admission.GetSnapshotsAsync([windowed.Key], CancellationToken.None));
        Assert.Equal(1, activeSnap.UsedUnits);
        Assert.Equal(10, activeSnap.ObservedLimit);
        Assert.Equal(9, activeSnap.ObservedRemaining);
        Assert.Equal(clock.Now.AddMinutes(1), activeSnap.WindowEndAt);

        // Once the fixed window expires, snapshot must clear stale usage and observations.
        clock.Now = clock.Now.AddMinutes(1);
        var expiredSnap = Assert.Single(await admission.GetSnapshotsAsync([windowed.Key], CancellationToken.None));
        Assert.Equal(0, expiredSnap.UsedUnits);
        Assert.Null(expiredSnap.WindowEndAt);
        Assert.Null(expiredSnap.ObservedLimit);
        Assert.Null(expiredSnap.ObservedRemaining);
    }

    [Fact]
    public async Task Worker_PropagatesCommandCorrelationIntoTransportHttpLog()
    {
        var clock = new ManualClock();
        var (schemaDb, keeper) = TestDbFactory.CreateSqliteDb();
        await using var _ = keeper;
        await using var __ = schemaDb;
        var dsn = TestDbFactory.SqliteDsn(schemaDb);

        var logger = new RecordingHttpLogger();
        IntegrationHttpTransport? transport = null;
        var dispatcher = new WorkerCorrelationDispatcher(async (req, ct) =>
        {
            var resp = await transport!.Bind(req.TransportAttempt!, Resolve).SendAsync(Request(effectful: true), ct);
            return resp.StatusCode == 200
                ? new IntegrationDispatchResult(IntegrationDispatchOutcome.Applied)
                : new IntegrationDispatchResult(IntegrationDispatchOutcome.Unknown);
        });

        var services = new ServiceCollection()
            .AddScoped(_ => TestDbFactory.CreateSqliteContext(dsn))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IAdapterHttpLogger>(logger)
            .AddSingleton<Delegat.Services.IOperationLogger, NoopOpLogger>()
            .AddSingleton<IIntegrationStageDispatcher>(dispatcher)
            .AddSingleton<IntegrationScopeAdmission>()
            .AddSingleton<IntegrationHttpTransport>();
        services.AddHttpClient(IntegrationHttpTransport.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") })));
        await using var provider = services.BuildServiceProvider();
        transport = provider.GetRequiredService<IntegrationHttpTransport>();

        string admittedOpId;
        await using (var admitDb = TestDbFactory.CreateSqliteContext(dsn))
        {
            var coordinator = new IntegrationCoordinator(admitDb, new NoopOpLogger(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IntegrationCoordinator>.Instance);
            var admitted = await coordinator.AdmitAsync(new IntegrationAdmitRequest(
                "export-product", StoreId: 5, AuthorUserId: 10, AuthorApiKeyId: null, ConnectionId: 23,
                RequestKey: "corr-key", InputJson: "{}", Title: "Corr test",
                Stages: [new IntegrationStageSeed("item-1", "create", "create", 0, Payload)]),
                v1Enabled: true, CancellationToken.None);
            admittedOpId = admitted.OperationId;
        }

        var worker = new IntegrationExecutionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IntegrationExecutionWorker>.Instance,
            clock);
        await worker.ProcessDueStagesAsync(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(admittedOpId, entry.OperationId);
        Assert.Equal(5, entry.StoreId);
        Assert.Equal(23, entry.ConnectionId);
    }

    private sealed class WorkerCorrelationDispatcher(
        Func<DispatchStageRequest, CancellationToken, Task<IntegrationDispatchResult>> onDispatch)
        : IIntegrationStageDispatcher
    {
        public Task<IntegrationDispatchResult> DispatchAsync(DispatchStageRequest request, CancellationToken ct) =>
            onDispatch(request, ct);
        public Task<IntegrationDispatchResult> ConfirmAsync(DispatchStageRequest request, CancellationToken ct) =>
            Task.FromResult(new IntegrationDispatchResult(IntegrationDispatchOutcome.Unknown));
    }

    private sealed class NoopOpLogger : Delegat.Services.IOperationLogger
    {
        public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
            int? connectionId = null, string? connectionName = null,
            string? batchId = null, string? batchType = null, int? totalItems = null, int? authorApiKeyId = null)
            => Task.CompletedTask;
        public Task LogAsync(string operationId, int storeId, string level, string message) => Task.CompletedTask;
        public Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed) => Task.CompletedTask;
        public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
        public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
    }
}
