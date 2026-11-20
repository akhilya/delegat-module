using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using Delegat.Marketplace.SDK;
using Delegat.Models.Integration;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Integration;

internal sealed record IntegrationTransportAttempt(
    int CommandId, int StageId, int AttemptId, int ClaimVersion, bool AllowEffects, DateTimeOffset ClaimExpiresAt);

internal sealed record IntegrationOutboundPolicy(
    bool IsEffectful, IReadOnlyList<IntegrationScope> Scopes, int ResponseLimitBytes);

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
    // SDK policy hints are not authoritative for scope identity or effect safety.
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
            request.Policy.RequestCost != 1m || policy.IsEffectful != request.Policy.IsEffectful)
            throw new IntegrationTransportBlockedException("transport_policy_missing_or_mismatched");
        if (policy.IsEffectful && !attempt.AllowEffects)
            throw new IntegrationTransportBlockedException("transport_effect_not_permitted");

        var leaseLifetime = attempt.ClaimExpiresAt - clock.GetUtcNow();
        if (leaseLifetime <= TimeSpan.Zero)
            throw new IntegrationTransportBlockedException("transport_attempt_or_scope_not_current");

        var reservation = await admission.TryReserveAsync(policy.Scopes, leaseLifetime, ct);
        if (reservation.Lease is not { } lease)
            throw new IntegrationTransportBlockedException("transport_admission_deferred", reservation.RetryAt);

        DateTimeOffset? cooldown = null;
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
            cooldown = RetryAfter(response, clock.GetUtcNow());
            var body = await ReadBodyAsync(response.Content, policy.ResponseLimitBytes, send.Token);
            var headers = response.Headers.Concat(response.Content.Headers)
                .GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.SelectMany(h => h.Value)), StringComparer.OrdinalIgnoreCase);
            return new((int)response.StatusCode, headers, body);
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
            // Caller cancellation must not discard a cooldown already seen in the headers.
            await admission.CompleteAsync(lease, cooldown, CancellationToken.None);
        }
    }

    private async Task<bool> AuthorizeAsync(IntegrationTransportAttempt attempt, IntegrationScopeLease lease,
        IntegrationTransportRequest request, bool isEffectful, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = dbScopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var keys = lease.ScopeKeys.ToArray();
        var nowUtc = now.UtcDateTime;
        var ticks = now.UtcTicks;
        var activeStages = db.integrationStages.Where(s => s.Id == attempt.StageId && s.commandId == attempt.CommandId &&
            s.state == IntegrationStageState.InFlight && s.claimVersion == attempt.ClaimVersion && s.claimedUntil > nowUtc &&
            (s.command.state == IntegrationCommandState.Admitted || s.command.state == IntegrationCommandState.Executing));
        if (isEffectful)
            activeStages = activeStages.Where(s => s.isEffectful && !s.command.stopRequested && s.payloadFileKey == null);

        // Keep the scope check in the fence statement; a captured query's Count can be evaluated before it.
        var eligible = db.integrationAttempts.Where(a => a.Id == attempt.AttemptId && a.commandId == attempt.CommandId &&
            a.stageId == attempt.StageId && a.completedAt == null && a.outcome == null &&
            activeStages.Any(s => s.attemptCount == a.attemptNumber) &&
            db.integrationScopeStates.Count(s => keys.Contains(s.scopeKey) && s.leaseOwner == lease.Owner &&
                s.leaseUntilTicks > ticks && s.cooldownUntilTicks <= ticks) == keys.Length);
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

    private static DateTimeOffset? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if ((int)response.StatusCode is not (429 or 503))
            return null;
        if (response.Headers.RetryAfter?.Delta is { } delta && delta >= TimeSpan.Zero)
            return delta > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + delta;
        return response.Headers.RetryAfter?.Date;
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
