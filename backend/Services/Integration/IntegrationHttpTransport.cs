using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Delegat.Marketplace.SDK;
using Delegat.Models.Integration;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Integration;

internal sealed record IntegrationTransportAttempt(
    int CommandId, int StageId, int AttemptId, int ClaimVersion, bool AllowEffects, DateTimeOffset ClaimExpiresAt);

internal enum IntegrationVendorHeaderProfile
{
    Standard = 0,
    Wildberries = 1,
    Ozon = 2,
    YandexMarket = 3,
    Avito = 4
}

internal sealed record IntegrationOutboundPolicy(
    bool IsEffectful,
    IReadOnlyList<IntegrationScope> Scopes,
    int ResponseLimitBytes,
    int? RequestBodyLimitBytes = null,
    IntegrationVendorHeaderProfile HeaderProfile = IntegrationVendorHeaderProfile.Standard,
    Func<int, string?, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, IntegrationScopeSettlement>>? SettleResponse = null);

// This call was blocked. Earlier writes, if any, are not proven absent.
internal sealed class IntegrationTransportBlockedException(string code, DateTimeOffset? retryAt = null) : Exception(code)
{
    internal string Code { get; } = code;
    internal DateTimeOffset? RetryAt { get; } = retryAt;
}

internal sealed class IntegrationHttpTransport(
    IServiceScopeFactory dbScopes,
    IntegrationScopeAdmission admission,
    IHttpClientFactory clients,
    TimeProvider clock)
{
    internal const string ClientName = "IntegrationV1";
    private static readonly TimeSpan MaxSendTime = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Upgrade", "Proxy-Authorization",
        "Proxy-Connection", "Keep-Alive", "TE", "Trailer", "Expect"
    };

    // The host resolver must validate the bound provider/account, destination and exact route.
    // SDK policy hints are not authoritative for scope identity, destination, effect, or costs.
    internal IMarketplaceTransport Bind(IntegrationTransportAttempt attempt,
        Func<IntegrationTransportRequest, IntegrationOutboundPolicy?> resolvePolicy) =>
        new BoundTransport(this, attempt, resolvePolicy);

    private sealed class BoundTransport(IntegrationHttpTransport transport, IntegrationTransportAttempt attempt,
        Func<IntegrationTransportRequest, IntegrationOutboundPolicy?> resolvePolicy) : IMarketplaceTransport
    {
        public Task<IntegrationTransportResponse> SendAsync(IntegrationTransportRequest request, CancellationToken ct = default) =>
            transport.SendAsync(attempt, request, resolvePolicy, ct);
    }

    private async Task<IntegrationTransportResponse> SendAsync(IntegrationTransportAttempt attempt,
        IntegrationTransportRequest request, Func<IntegrationTransportRequest, IntegrationOutboundPolicy?> resolvePolicy,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var message = CreateMessage(request);
        var policy = resolvePolicy(request);
        if (policy is null || policy.Scopes.Count == 0 || policy.ResponseLimitBytes <= 0 ||
            policy.IsEffectful != request.Policy.IsEffectful)
            throw new IntegrationTransportBlockedException("transport_policy_missing_or_mismatched");
        if (policy.IsEffectful && !attempt.AllowEffects)
            throw new IntegrationTransportBlockedException("transport_effect_not_permitted");
        if (policy.RequestBodyLimitBytes is { } bodyLimit && request.Body is not null &&
            Encoding.UTF8.GetByteCount(request.Body) > bodyLimit)
            throw new IntegrationTransportBlockedException("transport_request_body_too_large");

        var leaseLifetime = attempt.ClaimExpiresAt - clock.GetUtcNow();
        if (leaseLifetime <= TimeSpan.Zero)
            throw new IntegrationTransportBlockedException("transport_attempt_or_scope_not_current");

        var reservation = await admission.TryReserveAsync(policy.Scopes, leaseLifetime, ct);
        if (reservation.Lease is not { } lease)
            throw new IntegrationTransportBlockedException("transport_admission_deferred", reservation.RetryAt);

        DateTimeOffset? cooldown = null;
        Dictionary<string, IntegrationScopeSettlement>? settlements = null;
        try
        {
            using var client = clients.CreateClient(ClientName);
            var now = clock.GetUtcNow();
            var deadline = now + MaxSendTime;
            if (attempt.ClaimExpiresAt < deadline)
                deadline = attempt.ClaimExpiresAt;
            if (lease.ExpiresAt < deadline)
                deadline = lease.ExpiresAt;
            if (deadline <= now || !await AuthorizeAsync(attempt, lease, request, policy.IsEffectful, now, ct))
                throw new IntegrationTransportBlockedException("transport_attempt_or_scope_not_current");

            var remaining = deadline - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                throw new IntegrationTransportBlockedException("transport_attempt_or_scope_not_current");

            using var timeout = new CancellationTokenSource(remaining, clock);
            using var send = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            send.Token.ThrowIfCancellationRequested();
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, send.Token);
            var responseNow = clock.GetUtcNow();
            var headers = ExtractHeaders(response);
            cooldown = ParseCooldown(response, headers, policy.HeaderProfile, responseNow);
            var headerSettlement = ParseHeaderQuota(headers, policy.HeaderProfile, responseNow);
            if (headerSettlement is not null)
            {
                settlements = new Dictionary<string, IntegrationScopeSettlement>(StringComparer.Ordinal);
                foreach (var key in lease.ScopeKeys)
                    settlements[key] = headerSettlement;
            }

            string? body = null;
            try
            {
                body = await ReadBodyAsync(response.Content, policy.ResponseLimitBytes, send.Token);
                return new((int)response.StatusCode, headers, body);
            }
            finally
            {
                if (policy.SettleResponse is not null)
                {
                    var custom = policy.SettleResponse((int)response.StatusCode, body, headers);
                    if (custom.Count > 0)
                        settlements = MergeSettlements(lease, settlements, custom);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return TransportFailure();
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return TransportFailure();
        }
        finally
        {
            // Caller cancellation must not discard a cooldown or settlement already seen in the response.
            await admission.CompleteAsync(lease, settlements, cooldown, CancellationToken.None);
        }
    }

    private async Task<bool> AuthorizeAsync(IntegrationTransportAttempt attempt, IntegrationScopeLease lease,
        IntegrationTransportRequest request, bool isEffectful, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = dbScopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var singleLeasedKeys = lease.ScopeKeys
            .Where(k => !lease.Details.TryGetValue(k, out var d) || d.MaxConcurrency == 1)
            .ToArray();
        var multiLeasedKeys = lease.ScopeKeys
            .Where(k => lease.Details.TryGetValue(k, out var d) && d.MaxConcurrency > 1)
            .ToArray();
        var budgetOnlyKeys = lease.ScopeKeys
            .Where(k => lease.Details.TryGetValue(k, out var d) && d.MaxConcurrency == 0)
            .ToArray();
        var multiOwnerMarker = IntegrationScopeAdmission.FormatActiveLeaseMarker(lease.Owner);

        var nowUtc = now.UtcDateTime;
        var ticks = now.UtcTicks;
        var activeStages = db.integrationStages.Where(s => s.Id == attempt.StageId && s.commandId == attempt.CommandId &&
            s.state == IntegrationStageState.InFlight && s.claimVersion == attempt.ClaimVersion && s.claimedUntil > nowUtc &&
            (s.command.state == IntegrationCommandState.Admitted || s.command.state == IntegrationCommandState.Executing));
        if (isEffectful)
            activeStages = activeStages.Where(s => s.isEffectful && !s.command.stopRequested && s.payloadFileKey == null);

        // Keep every scope check inside the fence statement; a captured query's Count can be evaluated before it.
        var eligible = db.integrationAttempts.Where(a => a.Id == attempt.AttemptId && a.commandId == attempt.CommandId &&
            a.stageId == attempt.StageId && a.completedAt == null && a.outcome == null &&
            activeStages.Any(s => s.attemptCount == a.attemptNumber));

        if (singleLeasedKeys.Length > 0)
        {
            eligible = eligible.Where(_ =>
                db.integrationScopeStates.Count(s => singleLeasedKeys.Contains(s.scopeKey) &&
                    s.leaseOwner == lease.Owner && s.leaseUntilTicks > ticks && s.cooldownUntilTicks <= ticks)
                == singleLeasedKeys.Length);
        }
        if (multiLeasedKeys.Length > 0)
        {
            eligible = eligible.Where(_ =>
                db.integrationScopeStates.Count(s => multiLeasedKeys.Contains(s.scopeKey) &&
                    s.activeLeases != null && s.activeLeases.Contains(multiOwnerMarker) && s.cooldownUntilTicks <= ticks)
                == multiLeasedKeys.Length);
        }
        if (budgetOnlyKeys.Length > 0)
        {
            eligible = eligible.Where(_ =>
                db.integrationScopeStates.Count(s => budgetOnlyKeys.Contains(s.scopeKey) &&
                    s.cooldownUntilTicks <= ticks)
                == budgetOnlyKeys.Length);
        }

        if (!isEffectful)
            return await eligible.AnyAsync(ct);

        var hash = IntegrationCoordinator.ComputeHash(request.Body ?? "");
        // One durable, single-use fence. If the commit is ambiguous, do not replay the send.
        return await eligible.Where(a => a.sendAuthorizedAt == null && a.requestHash == hash)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.sendAuthorizedAt, (DateTime?)nowUtc), ct) == 1;
    }

    private static HttpRequestMessage CreateMessage(IntegrationTransportRequest request)
    {
        if (!request.Url.IsAbsoluteUri || request.Url.Scheme != Uri.UriSchemeHttps ||
            request.Url.UserInfo.Length != 0 || request.Url.Fragment.Length != 0)
            throw new IntegrationTransportBlockedException("transport_request_invalid");

        HttpRequestMessage message;
        try
        {
            message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            throw new IntegrationTransportBlockedException("transport_request_invalid");
        }

        try
        {
            if (request.Body is not null)
                message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
            foreach (var (name, value) in request.Headers)
            {
                if (ReservedHeaders.Contains(name) || value.AsSpan().ContainsAny('\r', '\n', '\0'))
                    throw new IntegrationTransportBlockedException("transport_request_invalid");
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (message.Content is null || !MediaTypeHeaderValue.TryParse(value, out var type))
                        throw new IntegrationTransportBlockedException("transport_request_invalid");
                    message.Content.Headers.ContentType = type;
                }
                else if (!message.Headers.TryAddWithoutValidation(name, value) &&
                         message.Content?.Headers.TryAddWithoutValidation(name, value) != true)
                {
                    throw new IntegrationTransportBlockedException("transport_request_invalid");
                }
            }
            return message;
        }
        catch
        {
            message.Dispose();
            throw;
        }
    }

    private static Dictionary<string, string> ExtractHeaders(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers)
            .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(h => h.Value)), StringComparer.OrdinalIgnoreCase);

    internal static DateTimeOffset? ParseCooldown(
        HttpResponseMessage response,
        IReadOnlyDictionary<string, string> headers,
        IntegrationVendorHeaderProfile profile,
        DateTimeOffset now)
    {
        var status = (int)response.StatusCode;
        var isThrottle = status is 429 or 503 || (profile == IntegrationVendorHeaderProfile.YandexMarket && status == 420);
        if (!isThrottle)
            return null;

        DateTimeOffset? maxCooldown = StandardRetryAfter(response, now);

        switch (profile)
        {
            case IntegrationVendorHeaderProfile.Wildberries:
                // WB X-Ratelimit-Retry is in SECONDS and must never be clamped to 60s.
                if (TryParseNonNegativeSeconds(headers, "X-Ratelimit-Retry", out var wbSeconds))
                    maxCooldown = MaxDate(maxCooldown, SafeAdd(now, TimeSpan.FromSeconds(wbSeconds)));
                break;

            case IntegrationVendorHeaderProfile.Ozon:
                // Ozon Item-Retry-After is in MINUTES, unlike general Retry-After seconds.
                if (TryParseNonNegativeSeconds(headers, "Item-Retry-After", out var itemMinutes))
                    maxCooldown = MaxDate(maxCooldown, SafeAdd(now, TimeSpan.FromMinutes(itemMinutes)));
                break;

            case IntegrationVendorHeaderProfile.YandexMarket:
                // Yandex 420 resource throttle carries X-RateLimit-Resource-Until (RFC822/HTTP date).
                if (TryParseHttpDate(headers, "X-RateLimit-Resource-Until", out var until))
                    maxCooldown = MaxDate(maxCooldown, until);
                break;
        }

        return maxCooldown;
    }

    private static IntegrationScopeSettlement? ParseHeaderQuota(
        IReadOnlyDictionary<string, string> headers,
        IntegrationVendorHeaderProfile profile,
        DateTimeOffset now)
    {
        long? limit = null;
        long? remaining = null;
        DateTimeOffset? resetAt = null;

        switch (profile)
        {
            case IntegrationVendorHeaderProfile.YandexMarket:
                if (TryParseNonNegativeLong(headers, "X-RateLimit-Resource-Limit", out var ymLimit))
                    limit = ymLimit;
                if (TryParseNonNegativeLong(headers, "X-RateLimit-Resource-Remaining", out var ymRem))
                    remaining = ymRem;
                if (TryParseHttpDate(headers, "X-RateLimit-Resource-Until", out var ymUntil) && ymUntil > now)
                    resetAt = ymUntil;
                break;

            case IntegrationVendorHeaderProfile.Avito:
                if (TryParseNonNegativeLong(headers, "X-RateLimit-Limit", out var avLimit))
                    limit = avLimit;
                if (TryParseNonNegativeLong(headers, "X-RateLimit-Remaining", out var avRem))
                    remaining = avRem;
                break;

            case IntegrationVendorHeaderProfile.Ozon:
                if (TryParseNonNegativeLong(headers, "Ratelimit-Remaining", out var ozRem))
                    remaining = ozRem;
                if (TryParseNonNegativeLong(headers, "Item-Rate-Limit-Remaining", out var ozItemRem))
                    remaining = remaining.HasValue ? Math.Min(remaining.Value, ozItemRem) : ozItemRem;
                break;
        }

        return limit is null && remaining is null && resetAt is null
            ? null
            : new IntegrationScopeSettlement(ObservedLimit: limit, ObservedRemaining: remaining, ObservedResetAt: resetAt);
    }

    private static Dictionary<string, IntegrationScopeSettlement> MergeSettlements(
        IntegrationScopeLease lease,
        Dictionary<string, IntegrationScopeSettlement>? existing,
        IReadOnlyDictionary<string, IntegrationScopeSettlement> custom)
    {
        var merged = existing is not null
            ? new Dictionary<string, IntegrationScopeSettlement>(existing, StringComparer.Ordinal)
            : new Dictionary<string, IntegrationScopeSettlement>(StringComparer.Ordinal);

        foreach (var (rawOrHashedKey, c) in custom)
        {
            var key = lease.Details.ContainsKey(rawOrHashedKey)
                ? rawOrHashedKey
                : IntegrationScopeAdmission.HashScopeKey(rawOrHashedKey);
            if (merged.TryGetValue(key, out var e))
            {
                merged[key] = new IntegrationScopeSettlement(
                    ActualCost: c.ActualCost ?? e.ActualCost,
                    CooldownUntil: MaxDate(e.CooldownUntil, c.CooldownUntil),
                    ObservedLimit: c.ObservedLimit ?? e.ObservedLimit,
                    ObservedRemaining: c.ObservedRemaining ?? e.ObservedRemaining,
                    ObservedResetAt: MaxDate(e.ObservedResetAt, c.ObservedResetAt));
            }
            else
            {
                merged[key] = c;
            }
        }
        return merged;
    }

    private static DateTimeOffset? StandardRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta >= TimeSpan.Zero)
            return SafeAdd(now, delta);
        return response.Headers.RetryAfter?.Date;
    }

    private static DateTimeOffset SafeAdd(DateTimeOffset now, TimeSpan delta) =>
        delta > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delta;

    private static DateTimeOffset? MaxDate(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);

    private static bool TryParseNonNegativeSeconds(IReadOnlyDictionary<string, string> headers, string name, out double value)
    {
        value = 0;
        return headers.TryGetValue(name, out var raw) &&
               double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
    }

    private static bool TryParseNonNegativeLong(IReadOnlyDictionary<string, string> headers, string name, out long value)
    {
        value = 0;
        return headers.TryGetValue(name, out var raw) &&
               long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
               value >= 0;
    }

    private static bool TryParseHttpDate(IReadOnlyDictionary<string, string> headers, string name, out DateTimeOffset value)
    {
        value = default;
        return headers.TryGetValue(name, out var raw) &&
               DateTimeOffset.TryParse(raw.Trim(), CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    private static async Task<string> ReadBodyAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit)
            throw new IOException("Provider response exceeded the configured limit.");
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                if (output.Length + read > limit)
                    throw new IOException("Provider response exceeded the configured limit.");
                output.Write(buffer, 0, read);
            }
            using var bounded = new ByteArrayContent(output.GetBuffer(), 0, (int)output.Length);
            bounded.Headers.ContentType = content.Headers.ContentType;
            try
            {
                return await bounded.ReadAsStringAsync(ct);
            }
            catch (InvalidOperationException)
            {
                throw new IOException("Provider response charset is invalid.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static IntegrationTransportResponse TransportFailure() => new(0, new Dictionary<string, string>(), null);
}
