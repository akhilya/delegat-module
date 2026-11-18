using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Delegat.Controllers;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delegat.Tests;

/// <summary>
/// Local restart/replay coverage over real in-process Kestrel hosts bound to
/// loopback port 0. Each host wires the production AI controllers, durable
/// worker, turn store and SSE framing against one file-backed SQLite database;
/// hosts are stopped and recreated over the same queue/journal. Only bearer
/// credential verification and the model/native transports are fakes - this is
/// host recreation, not an OS process kill or production Program startup.
/// </summary>
[Collection("EnvVars")]
public class AiDurableTurnHttpTests
{
    private const string AdminBearer = "fixture-admin";
    private const string OtherBearer = "fixture-other";

    [Fact]
    public async Task StructuredChat_PostAndReplay_PreserveCanonicalIdentityAcrossHostRestart()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;

        var hostA = await fixture.StartAsync();
        List<SseFrame> framesA;
        using (var stream = await SseSession.OpenAsync(hostA.Client,
            Json(HttpMethod.Post, "/api/ai/chat", new
            {
                sessionId = "http-structured",
                messages = new[]
                {
                    new { role = "user", content = new[] { new { type = "text", text = "Hello" } } },
                },
            }), ct))
        {
            framesA = await stream.ReadToEndAsync(ct);
        }
        Assert.NotEmpty(framesA);
        Assert.Equal(SseEventType.Done, framesA[^1].Type);
        Assert.Equal(Enumerable.Range(1, framesA.Count).Select(i => (long)i),
            framesA.Select(f => f.Sequence));
        Assert.All(framesA, f =>
        {
            Assert.Equal("chat", f.Json.GetProperty("phase").GetString());
            Assert.False(string.IsNullOrEmpty(f.Json.GetProperty("runId").GetString()));
            Assert.False(string.IsNullOrEmpty(f.Json.GetProperty("messageId").GetString()));
            Assert.True(f.Json.GetProperty("dbId").GetInt32() > 0);
        });
        Assert.Equal(1, hostA.ModelCalls());
        Assert.Equal(0, hostA.ToolCalls());
        await WaitForJobStatusAsync(fixture, "http-structured", "completed", ct);
        var runId = framesA[0].Json.GetProperty("runId").GetString()!;
        await hostA.DisposeAsync();

        // A fresh host over the same file replays the exact committed journal.
        var hostB = await fixture.StartAsync();
        List<SseFrame> framesB;
        using (var stream = await SseSession.OpenAsync(hostB.Client,
            new HttpRequestMessage(HttpMethod.Get, "/api/ai/chat/stream?sessionId=http-structured"), ct))
        {
            framesB = await stream.ReadToEndAsync(ct);
        }
        Assert.Equal(framesA.Select(f => f.Data), framesB.Select(f => f.Data));
        Assert.Equal(0, hostB.Producer.Invocations);
        Assert.Equal(0, hostB.ModelCalls());

        var text = framesA.First(f => f.Type == SseEventType.Text);
        using (var stream = await SseSession.OpenAsync(hostB.Client,
            new HttpRequestMessage(HttpMethod.Get,
                $"/api/ai/chat/stream?sessionId=http-structured&runId={runId}&afterSequence={text.Sequence}"), ct))
        {
            var suffix = await stream.ReadToEndAsync(ct);
            Assert.Equal(framesA.Where(f => f.Sequence > text.Sequence).Select(f => f.Data),
                suffix.Select(f => f.Data));
            Assert.DoesNotContain(suffix, f => f.Sequence <= text.Sequence);
        }
        using (var stream = await SseSession.OpenAsync(hostB.Client,
            new HttpRequestMessage(HttpMethod.Get,
                $"/api/ai/chat/stream?sessionId=http-structured&runId={Guid.NewGuid():N}&afterSequence={text.Sequence}"), ct))
        {
            var replay = await stream.ReadToEndAsync(ct);
            Assert.Equal(framesA.Select(f => f.Data), replay.Select(f => f.Data));
        }
        using (var stream = await SseSession.OpenAsync(hostB.Client,
            new HttpRequestMessage(HttpMethod.Get,
                $"/api/ai/chat/stream?sessionId=http-structured&runId={runId}&afterSequence={framesA[^1].Sequence}"), ct))
        {
            Assert.Empty(await stream.ReadToEndAsync(ct, allowEmpty: true));
        }
        Assert.Equal(0, hostB.Producer.Invocations);
        Assert.Equal(0, hostB.ModelCalls());

        using var history = await hostB.Client.GetAsync("/api/ai/conversations/http-structured", ct);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var conversation = await history.Content.ReadFromJsonAsync<JsonElement>(ct);
        var assistant = Assert.Single(conversation.GetProperty("messages").EnumerateArray(),
            m => m.GetProperty("role").GetString() == "assistant");
        Assert.Equal(framesA[0].Json.GetProperty("dbId").GetInt32(),
            assistant.GetProperty("dbId").GetInt32());
        Assert.Equal("HTTP reply", assistant.GetProperty("content").GetString());
        using var envelope = JsonDocument.Parse(assistant.GetProperty("toolCallsJson").GetString()!);
        Assert.Equal(framesA[0].Json.GetProperty("messageId").GetString(),
            envelope.RootElement.GetProperty("messageId").GetString());
        Assert.Equal(runId, envelope.RootElement.GetProperty("runId").GetString());
        Assert.Equal("chat", envelope.RootElement.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task CompletedPrefix_RestartedHttpHost_ResumesOnlyNextNativeStep()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;

        var hostA = await fixture.StartAsync(PauseAt.FirstDone);
        string proposal;
        await using (var seed = fixture.Context())
            proposal = await SeedPlanAsync(seed, "http-prefix", hostA.StoreId, hostA.UserId);

        List<SseFrame> prefix;
        using (var stream = await SseSession.OpenAsync(hostA.Client,
            Json(HttpMethod.Post, "/api/ai/execute-plan",
                new { sessionId = "http-prefix", proposalId = proposal }), ct))
        {
            prefix = await stream.ReadUntilAsync(f => f.Type == SseEventType.Tool
                && f.Body.GetProperty("status").GetString() == "done"
                && f.Body.GetProperty("stepIndex").GetInt32() == 0, ct);
            await hostA.Producer.Paused.Task.WaitAsync(ct);
        }
        Assert.Equal(1, hostA.ToolCalls());
        Assert.Equal("first", hostA.LastBody()!.Value.GetProperty("name").GetString());
        var runId = prefix[0].Json.GetProperty("runId").GetString()!;
        var messageId = prefix[0].Json.GetProperty("messageId").GetString()!;
        var dbId = prefix[0].Json.GetProperty("dbId").GetInt32();
        var cursor = prefix[^1].Sequence;
        await using (var inspect = fixture.Context())
        {
            var job = await inspect.aiTurnJobs.SingleAsync();
            Assert.Equal("running", job.status);
            Assert.Null(job.startedStep);
            Assert.Single(JsonSerializer.Deserialize<List<AiExecutionStepCheckpoint>>(
                job.checkpointsJson, AiDurableTurnStore.Json)!);
        }
        await hostA.DisposeAsync();
        await fixture.ExpireLeaseAsync("http-prefix");

        // HostB owns the lease and resumes only the uncommitted step.
        var hostB = await fixture.StartAsync();
        List<SseFrame> suffix;
        using (var stream = await SseSession.OpenAsync(hostB.Client,
            new HttpRequestMessage(HttpMethod.Get,
                $"/api/ai/chat/stream?sessionId=http-prefix&runId={runId}&afterSequence={cursor}"), ct))
        {
            suffix = await stream.ReadToEndAsync(ct);
        }
        var suffixTools = suffix.Where(f => f.Type == SseEventType.Tool)
            .Select(f => f.Body.GetProperty("stepIndex").GetInt32()).ToList();
        Assert.Equal(new[] { 1, 1 }, suffixTools);
        Assert.Equal(1, hostB.Producer.Invocations);
        Assert.Equal(1, hostB.ToolCalls());
        Assert.Equal("second", hostB.LastBody()!.Value.GetProperty("name").GetString());
        Assert.Equal(0, hostB.ModelCalls());
        Assert.Equal(0, hostA.ModelCalls());

        List<SseFrame> full;
        using (var stream = await SseSession.OpenAsync(hostB.Client,
            new HttpRequestMessage(HttpMethod.Get, "/api/ai/chat/stream?sessionId=http-prefix"), ct))
        {
            full = await stream.ReadToEndAsync(ct);
        }
        Assert.Equal(Enumerable.Range(1, full.Count).Select(i => (long)i),
            full.Select(f => f.Sequence));
        Assert.Equal(new[] { 0, 1 }, full.Where(f => f.Type == SseEventType.Tool
            && f.Body.GetProperty("status").GetString() == "done")
            .Select(f => f.Body.GetProperty("stepIndex").GetInt32()));
        Assert.Equal(proposal, full[^1].Json.GetProperty("proposalId").GetString());

        using var history = await hostB.Client.GetAsync("/api/ai/conversations/http-prefix", ct);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var conversation = await history.Content.ReadFromJsonAsync<JsonElement>(ct);
        var assistant = Assert.Single(conversation.GetProperty("messages").EnumerateArray(),
            m => m.GetProperty("role").GetString() == "assistant");
        Assert.Equal(dbId, assistant.GetProperty("dbId").GetInt32());
        using var envelope = JsonDocument.Parse(assistant.GetProperty("toolCallsJson").GetString()!);
        var turn = envelope.RootElement;
        Assert.Equal(messageId, turn.GetProperty("messageId").GetString());
        Assert.Equal(runId, turn.GetProperty("runId").GetString());
        Assert.Equal("execution", turn.GetProperty("phase").GetString());
        Assert.True(turn.GetProperty("planExecuted").GetBoolean());
        Assert.Equal(99, turn.GetProperty("plan").GetProperty("affectedCount").GetInt32());
        Assert.Equal(proposal, turn.GetProperty("plan").GetProperty("proposalId").GetString());
        Assert.Equal(2, turn.GetProperty("timeline").EnumerateArray()
            .Count(e => e.GetProperty("type").GetString() == "tool"));
        Assert.Equal(2, turn.GetProperty("timeline").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() == "tool")
            .Select(e => e.GetProperty("id").GetString()).Distinct().Count());
        Assert.All(turn.GetProperty("tools").EnumerateArray(),
            t => Assert.Equal("platform_request", t.GetProperty("name").GetString()));

        int audits;
        await using (var inspect = fixture.Context())
            audits = await inspect.aiAgentAudits.CountAsync();
        using (var repeat = await hostB.Client.SendAsync(
            Json(HttpMethod.Post, "/api/ai/execute-plan",
                new { sessionId = "http-prefix", proposalId = proposal }), ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
            var error = await repeat.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal("plan_not_pending_or_mismatched", error.GetProperty("code").GetString());
        }
        await using (var inspect = fixture.Context())
        {
            Assert.Single(await inspect.aiTurnJobs.ToListAsync());
            Assert.Equal(audits, await inspect.aiAgentAudits.CountAsync());
        }
        Assert.Equal(1, hostB.ToolCalls());
        Assert.Equal(0, hostB.ModelCalls());
    }

    [Fact]
    public async Task StartedPrefix_RestartedHost_ReportsUnknownWithoutDispatch()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;

        var hostA = await fixture.StartAsync(PauseAt.FirstRunning);
        string proposal;
        await using (var seed = fixture.Context())
            proposal = await SeedPlanAsync(seed, "http-started", hostA.StoreId, hostA.UserId);

        using (var stream = await SseSession.OpenAsync(hostA.Client,
            Json(HttpMethod.Post, "/api/ai/execute-plan",
                new { sessionId = "http-started", proposalId = proposal }), ct))
        {
            await stream.ReadUntilAsync(f => f.Type == SseEventType.Tool
                && f.Body.GetProperty("status").GetString() == "running"
                && f.Body.GetProperty("stepIndex").GetInt32() == 0, ct);
            await hostA.Producer.Paused.Task.WaitAsync(ct);
        }
        // The pause boundary is before the next producer MoveNext: nothing dispatched.
        Assert.Equal(0, hostA.ToolCalls());
        await using (var inspect = fixture.Context())
        {
            var job = await inspect.aiTurnJobs.SingleAsync();
            Assert.Equal(0, job.startedStep);
        }
        await hostA.DisposeAsync();
        await fixture.ExpireLeaseAsync("http-started");

        var hostB = await fixture.StartAsync();
        List<SseFrame> frames;
        using (var stream = await SseSession.OpenAsync(hostB.Client,
            new HttpRequestMessage(HttpMethod.Get, "/api/ai/chat/stream?sessionId=http-started"), ct))
        {
            frames = await stream.ReadToEndAsync(ct);
        }
        var status = Assert.Single(frames, f => f.Type == SseEventType.ExecutionStatus);
        Assert.Equal("interrupted", status.Body.GetProperty("status").GetString());
        Assert.True(status.Body.GetProperty("outcomeUnknown").GetBoolean());
        Assert.Equal(0, status.Body.GetProperty("succeeded").GetInt32());
        Assert.Equal(2, status.Body.GetProperty("total").GetInt32());
        Assert.Equal(SseEventType.Done, frames[^1].Type);
        Assert.True(frames[^1].Body.GetProperty("interrupted").GetBoolean());
        Assert.Equal(0, hostB.Producer.Invocations);
        Assert.Equal(0, hostB.ToolCalls());
        Assert.Equal(0, hostB.ModelCalls());
        await using (var inspect = fixture.Context())
            Assert.Equal("interrupted", (await inspect.aiTurnJobs.SingleAsync()).status);

        using (var stream = await SseSession.OpenAsync(hostB.Client,
            new HttpRequestMessage(HttpMethod.Get, "/api/ai/chat/stream?sessionId=http-started"), ct))
        {
            await stream.ReadToEndAsync(ct);
        }
        Assert.Equal(0, hostB.Producer.Invocations);
        Assert.Equal(0, hostB.ToolCalls());
        Assert.Equal(0, hostB.ModelCalls());
    }

    [Fact]
    public async Task StopAndOwnership_AreEnforcedOverHttp()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;

        var hostA = await fixture.StartAsync(PauseAt.Text);
        var stream = await SseSession.OpenAsync(hostA.Client,
            Json(HttpMethod.Post, "/api/ai/chat", new
            {
                sessionId = "http-stop",
                messages = new[] { new { role = "user", content = "Stop this turn" } },
            }), ct);
        try
        {
            await stream.ReadUntilAsync(f => f.Type == SseEventType.Text, ct);
            await hostA.Producer.Paused.Task.WaitAsync(ct);

            using (var anonymous = new HttpClient(NoCookies())
            {
                BaseAddress = hostA.Client.BaseAddress, Timeout = Timeout.InfiniteTimeSpan,
            })
            using (var denied = await anonymous.GetAsync(
                "/api/ai/chat/stream?sessionId=http-stop", ct))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            }
            using (var other = await hostA.Client.SendAsync(
                Owned(HttpMethod.Get, "/api/ai/chat/stream?sessionId=http-stop",
                    OtherBearer, hostA.StoreId), ct))
            {
                Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
            }
            using (var foreign = await hostA.Client.SendAsync(
                WithCookie(HttpMethod.Get, "/api/ai/chat/stream?sessionId=http-stop", 999), ct))
            {
                Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            }

            using (var foreignStop = await hostA.Client.SendAsync(
                Owned(HttpMethod.Post, "/api/ai/stop", OtherBearer, hostA.StoreId,
                    new { sessionId = "http-stop" }), ct))
            {
                Assert.Equal(HttpStatusCode.OK, foreignStop.StatusCode);
                var body = await foreignStop.Content.ReadFromJsonAsync<JsonElement>(ct);
                Assert.False(body.GetProperty("stopped").GetBoolean());
            }
            await using (var inspect = fixture.Context())
                Assert.False((await inspect.aiTurnJobs.SingleAsync()).cancelRequested);

            using (var stop = await hostA.Client.SendAsync(
                Json(HttpMethod.Post, "/api/ai/stop", new { sessionId = "http-stop" }), ct))
            {
                Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
                var body = await stop.Content.ReadFromJsonAsync<JsonElement>(ct);
                Assert.True(body.GetProperty("stopped").GetBoolean());
            }

            await stream.ReadToEndAsync(ct);
            Assert.Contains(stream.Frames, f => f.Type == SseEventType.Stopped);
            var job = await WaitForJobStatusAsync(fixture, "http-stop", "stopped", ct);
            Assert.True(job.cancelRequested);
            await using (var inspect = fixture.Context())
            {
                var row = await inspect.aiConversationMessages.SingleAsync(m => m.role == "assistant");
                Assert.True(AiConversationTurnStore.HasStoppedTurn(row.toolCallsJson));
            }
            Assert.Equal(0, hostA.ToolCalls());
        }
        finally
        {
            stream.Dispose();
        }
    }

    /// <summary>
    /// The single narrow entry point used by the Delegat.AiHttpTestHost console
    /// process: validates the parent-created state directory, starts one real
    /// host over the supplied file database, seeds the browser scenario once,
    /// publishes ready.json and then parks on the app's shutdown signal. The
    /// child never deletes the caller's state directory.
    /// </summary>
    internal static async Task RunBrowserHostAsync(
        string stateDirectory, string staticRoot, string sessionId, int port,
        string pauseMode, string controlToken, CancellationToken ct)
    {
        var options = new StandaloneOptions(
            Path.GetFullPath(stateDirectory), Path.GetFullPath(staticRoot),
            sessionId, port, ParsePause(pauseMode), controlToken);
        ValidateStandaloneOptions(options);

        await using var fixture = await HttpFixture.CreateAsync(options);
        var host = await fixture.StartAsync();
        try
        {
            await EnsureBrowserScenarioAsync(fixture, host, options, ct);
            await WriteReadyFileAsync(options, host, ct);
            await host.WaitForShutdownAsync(ct);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private sealed record StandaloneOptions(
        string StateDirectory, string StaticRoot, string SessionId, int Port,
        PauseAt Pause, string ControlToken);

    private static PauseAt ParsePause(string mode) => mode switch
    {
        "none" => PauseAt.None,
        "first-done" => PauseAt.FirstDone,
        "first-running" => PauseAt.FirstRunning,
        "review-text" => PauseAt.Text,
        var other => throw new ArgumentException($"Unknown pause mode '{other}'."),
    };

    private static void ValidateStandaloneOptions(StandaloneOptions options)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath().TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dir = options.StateDirectory;
        var parent = Path.GetFullPath(Path.GetDirectoryName(dir) ?? Path.Combine(tempRoot, ".."));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var name = Path.GetFileName(dir);
        const string prefix = "delegat-ai-browser-";
        var validName = name.StartsWith(prefix, StringComparison.Ordinal)
            && name.Length > prefix.Length
            && name[prefix.Length..].All(char.IsAsciiLetterOrDigit);
        if (!string.Equals(parent, tempRoot, pathComparison)
            || !validName
            || !Directory.Exists(dir)
            || (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException(
                $"The state directory must be a real direct delegat-ai-browser-* child of {tempRoot}.");
        var tokenFile = Path.Combine(dir, "owner.token");
        var token = File.Exists(tokenFile) ? File.ReadAllText(tokenFile).Trim() : "";
        if (token.Length < 32 || token != options.ControlToken)
            throw new ArgumentException("The supplied control token does not match owner.token.");
        if (options.SessionId.Length is < 1 or > 36)
            throw new ArgumentException("sessionId must be 1..36 characters.");
        if (options.Port is < 0 or > 65535)
            throw new ArgumentException("port must be in 0..65535.");
        if (!File.Exists(Path.Combine(options.StaticRoot, "index.html")))
            throw new ArgumentException($"The static root has no index.html: {options.StaticRoot}");
    }

    /// <summary>
    /// Creates the owned user text, signed plan and conversation title exactly
    /// once; a restarted host finds the session and never rewrites history.
    /// </summary>
    private static async Task EnsureBrowserScenarioAsync(
        HttpFixture fixture, HttpHost host, StandaloneOptions options, CancellationToken ct)
    {
        await using var db = fixture.Context();
        if (await db.aiConversations.AnyAsync(c => c.sessionId == options.SessionId, ct))
            return;
        await AiConversationTurnStore.AppendUserIfNeededAsync(db, options.SessionId,
            host.StoreId, host.UserId, new ChatMessage
            {
                role = "user",
                content = JsonSerializer.SerializeToElement("Update two local fixture records"),
            }, ct);
        await SeedPlanAsync(db, options.SessionId, host.StoreId, host.UserId);
        var conversation = await db.aiConversations
            .SingleAsync(c => c.sessionId == options.SessionId, ct);
        conversation.title = "Local durable integration";
        var planMessage = await db.aiConversationMessages
            .Where(m => m.conversationId == conversation.Id && m.role == "assistant")
            .SingleAsync(ct);
        var envelope = JsonNode.Parse(planMessage.toolCallsJson!)!;
        var plan = envelope["plan"]!.AsObject();
        plan["summary"] ??= "Update two local fixture records";
        planMessage.toolCallsJson = envelope.ToJsonString();
        await db.SaveChangesAsync(ct);
    }

    private static async Task WriteReadyFileAsync(
        StandaloneOptions options, HttpHost host, CancellationToken ct)
    {
        var ready = Path.Combine(options.StateDirectory, "ready.json");
        var temp = ready + ".tmp";
        var payload = JsonSerializer.Serialize(new
        {
            processId = Environment.ProcessId,
            url = host.Client.BaseAddress!.ToString().TrimEnd('/'),
            sessionId = options.SessionId,
            userId = host.UserId,
            storeId = host.StoreId,
        });
        await File.WriteAllTextAsync(temp, payload + "\n", new UTF8Encoding(false), ct);
        File.Move(temp, ready, overwrite: true);
    }

    private static HttpClientHandler NoCookies() => new() { UseCookies = false };

    private static HttpRequestMessage Json(HttpMethod method, string path, object body) =>
        new(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            Headers = { { "X-UI-Locale", "en" } },
        };

    private static HttpRequestMessage Owned(
        HttpMethod method, string path, string bearer, int storeId, object? body = null)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        message.Headers.TryAddWithoutValidation("Cookie", $"selected_store_id={storeId}");
        if (body != null)
            message.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return message;
    }

    private static HttpRequestMessage WithCookie(HttpMethod method, string path, int storeId)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.TryAddWithoutValidation("Cookie", $"selected_store_id={storeId}");
        return message;
    }

    private static async Task<AiTurnJob> WaitForJobStatusAsync(
        HttpFixture fixture, string session, string status, CancellationToken ct)
    {
        while (true)
        {
            await using var db = fixture.Context();
            var job = await db.aiTurnJobs.AsNoTracking()
                .SingleOrDefaultAsync(j => j.sessionId == session, ct);
            if (job?.status == status) return job;
            await Task.Delay(100, ct);
        }
    }

    private static async Task<string> SeedPlanAsync(
        DataContext db, string session, int storeId, int userId)
    {
        var conversation = await AiConversationTurnStore.EnsureConversationAsync(
            db, session, storeId, userId, default);
        var steps = JsonNode.Parse("""
            [{"tool":"platform_request","description":"First","args":{"method":"PATCH","path":"/products/1","body":{"name":"first"}}},
             {"tool":"platform_request","description":"Second","args":{"method":"PATCH","path":"/products/1","body":{"name":"second"}}}]
            """)!.AsArray();
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(15);
        var hash = AiConversationTurnStore.ComputeStepsHash(steps);
        var (token, nonce) = AiPlanProposalToken.Mint(session, storeId, userId, hash, expires);
        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conversation.Id, role = "assistant", content = "Confirm",
            toolCallsJson = new JsonObject
            {
                ["_ui"] = "assistantTurn", ["tools"] = new JsonArray(), ["timeline"] = new JsonArray(),
                ["plan"] = new JsonObject
                {
                    ["proposalId"] = token, ["proposalNonce"] = nonce, ["stepsHash"] = hash,
                    ["createdAtUtc"] = now, ["expiresAtUtc"] = expires, ["steps"] = steps,
                    ["affectedCount"] = 99,
                },
            }.ToJsonString(),
        });
        await db.SaveChangesAsync();
        return token;
    }

    private sealed record SseFrame(string Data, JsonElement Json)
    {
        public string Type => Json.GetProperty("type").GetString()!;
        public JsonElement Body => Json.GetProperty("data");
        public long Sequence => Json.GetProperty("sequence").GetInt64();
    }

    /// <summary>One open SSE response: keeps the stream, reader and response together.</summary>
    private sealed class SseSession : IDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly StreamReader _reader;

        private SseSession(HttpResponseMessage response, StreamReader reader)
        {
            _response = response;
            _reader = reader;
        }

        public List<SseFrame> Frames { get; } = [];

        public static async Task<SseSession> OpenAsync(
            HttpClient client, HttpRequestMessage request, CancellationToken ct)
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            try
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
                return new SseSession(
                    response, new StreamReader(await response.Content.ReadAsStreamAsync(ct)));
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        public async Task<List<SseFrame>> ReadUntilAsync(
            Func<SseFrame, bool> stop, CancellationToken ct)
        {
            while (await ReadAsync(ct) is { } frame)
                if (stop(frame))
                    return Frames;
            throw new InvalidOperationException("The SSE stream ended before the expected frame.");
        }

        public async Task<List<SseFrame>> ReadToEndAsync(CancellationToken ct, bool allowEmpty = false)
        {
            while (await ReadAsync(ct) != null) { }
            if (Frames.Count == 0 && allowEmpty) return Frames;
            if (Frames.Count == 0 || Frames[^1].Type is not (SseEventType.Done or SseEventType.Stopped))
                throw new InvalidDataException("The SSE stream ended before a terminal event.");
            return Frames;
        }

        private async Task<SseFrame?> ReadAsync(CancellationToken ct)
        {
            var payload = new StringBuilder();
            var hasData = false;
            while (true)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null)
                {
                    if (hasData) throw new InvalidDataException("The SSE stream ended inside a frame.");
                    return null;
                }
                if (line.Length == 0)
                {
                    if (!hasData) continue;
                    var data = payload.ToString();
                    using var document = JsonDocument.Parse(data);
                    var frame = new SseFrame(data, document.RootElement.Clone());
                    Frames.Add(frame);
                    return frame;
                }
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                if (hasData) payload.Append('\n');
                var value = line["data:".Length..];
                if (value.StartsWith(' ')) value = value[1..];
                payload.Append(value);
                hasData = true;
            }
        }

        public void Dispose()
        {
            _reader.Dispose();
            _response.Dispose();
        }
    }

    private enum PauseAt { None, FirstRunning, FirstDone, Text }

    /// <summary>
    /// The real shared executor behind an intentional fault boundary: the pause
    /// fires only after the worker has committed the yielded event.
    /// </summary>
    private sealed class ControlledProducer(
        AiChatService chat, PauseAt pause, bool includeReview = false) : IAiDurableTurnProducer
    {
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Invocations;

        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnJob job, AiDurableTurnRequest request, AiExecutionResume resume,
            [EnumeratorCancellation] CancellationToken ct)
        {
            Interlocked.Increment(ref Invocations);
            var source = job.kind == "execution"
                ? chat.ExecutePlanEventsAsync(request.Steps, job.storeId, job.userId,
                    request.ProposalId!, ct,
                    sessionId: includeReview ? job.sessionId : null,
                    locale: includeReview ? request.Locale ?? "en" : "en",
                    resume: resume, runtimeOverride: job.engine)
                : chat.StreamChatEventsAsync(request.Messages, job.storeId, job.userId,
                    request.ModelId, request.PageContext, job.sessionId, request.Locale,
                    ct, runtimeOverride: job.engine);
            await foreach (var item in source.WithCancellation(ct))
            {
                yield return item;
                // Match only first step ordinal0 running/done, or Text. After yield resumes,
                // worker AppendAsync has COMMITTED that event. This is the intentional fault boundary.
                if (MatchesPause(item, pause))
                {
                    Paused.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
            }
        }

        private static bool MatchesPause(AiTurnEvent item, PauseAt pause) => pause switch
        {
            PauseAt.FirstRunning => item.Type == SseEventType.Tool
                && item.Data.TryGetProperty("status", out var running) && running.GetString() == "running"
                && item.Data.TryGetProperty("stepIndex", out var started) && started.GetInt32() == 0,
            PauseAt.FirstDone => item.Type == SseEventType.Tool
                && item.Data.TryGetProperty("status", out var done) && done.GetString() == "done"
                && item.Data.TryGetProperty("stepIndex", out var finished) && finished.GetInt32() == 0,
            PauseAt.Text => item.Type == SseEventType.Text,
            _ => false,
        };
    }

    /// <summary>Fixture credential validation only - not production JWT proof.</summary>
    private sealed class FixtureAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        FixtureAuthContext auth)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            var userId = header == $"Bearer {AdminBearer}" ? auth.AdminUserId
                : header == $"Bearer {OtherBearer}" ? 999
                : (int?)null;
            if (userId == null)
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("userId", userId.Value.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()),
                new Claim(Permissions.ClaimType, Permissions.AiUse),
                new Claim("is_admin", "true"),
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class FixtureAuthContext(int adminUserId)
    {
        public int AdminUserId { get; } = adminUserId;
    }

    private sealed class AiControllersOnly : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var keep = new HashSet<TypeInfo>
            {
                typeof(AiController).GetTypeInfo(),
                typeof(AiConversationController).GetTypeInfo(),
            };
            for (var i = feature.Controllers.Count - 1; i >= 0; i--)
                if (!keep.Contains(feature.Controllers[i]))
                    feature.Controllers.RemoveAt(i);
        }
    }

    /// <summary>One stopped-or-live Kestrel host over the shared file database.</summary>
    private sealed class HttpHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly (AiChatService Chat, int UserId, int StoreId,
            Func<int> ToolCalls, Func<JsonElement?> LastBody, Func<int> ModelCalls,
            IAsyncDisposable Lifetime) _runtime;
        private int _disposed;

        public HttpHost(WebApplication app, HttpClient client,
            (AiChatService Chat, int UserId, int StoreId, Func<int> ToolCalls,
                Func<JsonElement?> LastBody, Func<int> ModelCalls,
                IAsyncDisposable Lifetime) runtime, ControlledProducer producer)
        {
            _app = app;
            Client = client;
            _runtime = runtime;
            Producer = producer;
        }

        public HttpClient Client { get; }
        public ControlledProducer Producer { get; }
        public int UserId => _runtime.UserId;
        public int StoreId => _runtime.StoreId;
        public Func<int> ToolCalls => _runtime.ToolCalls;
        public Func<JsonElement?> LastBody => _runtime.LastBody;
        public Func<int> ModelCalls => _runtime.ModelCalls;

        public Task WaitForShutdownAsync(CancellationToken ct) => _app.WaitForShutdownAsync(ct);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await DisposeHostAsync(_app, Client, _runtime.Lifetime);
        }
    }

    private static async ValueTask DisposeHostAsync(
        WebApplication? app, HttpClient? client, IAsyncDisposable lifetime)
    {
        try
        {
            try { client?.Dispose(); }
            finally
            {
                if (app != null)
                {
                    try
                    {
                        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await app.StopAsync(stop.Token);
                    }
                    finally { await app.DisposeAsync(); }
                }
            }
        }
        finally { await lifetime.DisposeAsync(); }
    }

    /// <summary>
    /// The shared file database plus a lifecycle for fresh hosts. Lease expiry is
    /// advanced explicitly after disposing the old host; it is simulated, never
    /// a wall-clock failover claim.
    /// </summary>
    private sealed class HttpFixture : IAsyncDisposable
    {
        private readonly string _dir;
        private readonly string _dsn;
        private readonly bool _ownsDirectory;
        private readonly StandaloneOptions? _standalone;
        private readonly object _ledgerLock = new();
        private readonly string? _oldAiEnabled;
        private readonly string? _oldKey;
        private readonly List<HttpHost> _hosts = [];

        private HttpFixture(StandaloneOptions? standalone)
        {
            _standalone = standalone;
            _ownsDirectory = standalone == null;
            _dir = standalone?.StateDirectory
                ?? Path.Combine(Path.GetTempPath(), $"delegat-ai-http-{Guid.NewGuid():N}");
            if (_ownsDirectory) Directory.CreateDirectory(_dir);
            _dsn = $"Data Source={Path.Combine(_dir, "turns.db")};Default Timeout=10;Pooling=False";
            _oldAiEnabled = Environment.GetEnvironmentVariable("AI_ENABLED");
            _oldKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY");
            Environment.SetEnvironmentVariable("AI_ENABLED", "true");
            Environment.SetEnvironmentVariable("ENCRYPTION_KEY",
                Convert.ToBase64String(Enumerable.Range(1, 32).Select(x => (byte)x).ToArray()));
        }

        public static async Task<HttpFixture> CreateAsync(StandaloneOptions? standalone = null)
        {
            var fixture = new HttpFixture(standalone);
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await using var db = fixture.Context();
                await db.Database.EnsureCreatedAsync(deadline.Token);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public DataContext Context() => TestDbFactory.CreateSqliteContext(_dsn);

        public async Task<HttpHost> StartAsync(PauseAt pause = PauseAt.None)
        {
            var standalone = _standalone;
            var runtime = standalone == null
                ? await AiChatAgentEvaluationTests.CreateDurableHttpRuntimeAsync()
                // Standalone hosts run the shared executor/review over the same
                // file database and record every fake transport call on disk.
                : await AiChatAgentEvaluationTests.CreateDurableHttpRuntimeAsync(_dsn,
                    nativeCall: body => WriteLedger("native-calls.ndjson",
                        new { processId = Environment.ProcessId, body }),
                    modelCall: () => WriteLedger("model-calls.ndjson",
                        new { processId = Environment.ProcessId }));
            WebApplication? app = null;
            HttpClient? client = null;
            try
            {
                var producer = new ControlledProducer(
                    runtime.Chat, standalone?.Pause ?? pause, includeReview: standalone != null);
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    EnvironmentName = "Testing",
                    WebRootPath = standalone?.StaticRoot,
                });
                builder.Configuration.Sources.Clear();
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options =>
                    options.Listen(IPAddress.Loopback, standalone?.Port ?? 0));

                var services = builder.Services;
                services.AddControllers()
                    .AddApplicationPart(typeof(AiController).Assembly)
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new AiControllersOnly()))
                    .AddControllersAsServices();
                services.AddScoped(_ => Context());
                services.AddScoped<AiDurableTurnStore>();
                services.AddSingleton<AiLiveTurnHub>();
                services.AddSingleton<IAiDurableTurnProducer>(producer);
                services.AddHostedService<AiDurableTurnWorker>();
                services.AddHttpContextAccessor();
                services.AddSingleton(new FixtureAuthContext(runtime.UserId));
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddScheme<AuthenticationSchemeOptions, FixtureAuthHandler>(
                        JwtBearerDefaults.AuthenticationScheme, _ => { });
                services.AddAuthorization();
                services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
                services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
                services.AddScoped<ICurrentUserService>(sp =>
                {
                    var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
                    var raw = http?.User.FindFirst("userId")?.Value
                        ?? http?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    int? userId = int.TryParse(raw, out var id) ? id : null;
                    int? storeId = http?.Request.Cookies.TryGetValue("selected_store_id", out var cookie) == true
                        && int.TryParse(cookie, out var parsed) ? parsed : null;
                    return new FakeCurrentUserService { UserId = userId, SelectedStoreId = storeId };
                });
                services.AddScoped(sp => new AiController(
                    runtime.Chat, null!, sp.GetRequiredService<ICurrentUserService>(),
                    sp.GetRequiredService<AiLiveTurnHub>(), null!, null!,
                    sp.GetRequiredService<AiDurableTurnStore>(), builder.Configuration));
                services.AddScoped(sp => new AiConversationController(
                    sp.GetRequiredService<DataContext>(),
                    sp.GetRequiredService<ICurrentUserService>(), runtime.Chat,
                    sp.GetRequiredService<AiLiveTurnHub>()));

                app = builder.Build();
                // Minimal fixture exception bridge: actual ApiException to status+code JSON.
                app.Use(async (context, next) =>
                {
                    try
                    {
                        await next(context);
                    }
                    catch (ApiException ex) when (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = ex.StatusCode;
                        await context.Response.WriteAsJsonAsync(new { code = ex.Code });
                    }
                });
                if (standalone != null)
                {
                    app.UseDefaultFiles();
                    app.UseStaticFiles();
                }
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapGroup("/api").MapControllers();
                if (standalone != null)
                {
                    MapTestControls(app, standalone, producer);
                    // Unmatched API requests return a real 404; only the SPA
                    // shell falls back to index.html.
                    app.Map("/api/{**path}", () => Results.NotFound());
                    app.MapFallbackToFile("index.html");
                }
                using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    await app.StartAsync(startup.Token);

                var address = app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                client = new HttpClient(new HttpClientHandler { UseCookies = false })
                {
                    BaseAddress = new Uri(address),
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", AdminBearer);
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Cookie", $"selected_store_id={runtime.StoreId}");

                var host = new HttpHost(app, client, runtime, producer);
                _hosts.Add(host);
                return host;
            }
            catch
            {
                await DisposeHostAsync(app, client, runtime.Lifetime);
                throw;
            }
        }

        public async Task ExpireLeaseAsync(string session)
        {
            await using var db = Context();
            await db.aiTurnJobs
                .Where(j => j.sessionId == session && j.activeSessionId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    j => j.leaseExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        }

        /// <summary>
        /// Token-gated control surface, registered only for standalone hosts.
        /// It exposes read-only durable state and a graceful shutdown; it never
        /// mutates jobs or leases.
        /// </summary>
        private void MapTestControls(
            WebApplication app, StandaloneOptions options, ControlledProducer producer)
        {
            bool Authorized(HttpContext http) => string.Equals(
                http.Request.Headers["X-Ai-Test-Control"].ToString(),
                options.ControlToken, StringComparison.Ordinal);

            app.MapGet("/__test/state", async (HttpContext http) =>
            {
                if (!Authorized(http)) return Results.NotFound();
                await using var db = Context();
                var jobs = await db.aiTurnJobs.AsNoTracking()
                    .Where(j => j.sessionId == options.SessionId)
                    .OrderBy(j => j.Id)
                    .Select(j => new
                    {
                        j.Id, j.runId, j.status, j.startedStep, j.executionEnded,
                        j.lastSequence, j.leaseExpiresAtUtc, j.checkpointsJson, j.bindingJson,
                    })
                    .ToListAsync(http.RequestAborted);
                return Results.Json(new
                {
                    processId = Environment.ProcessId,
                    paused = producer.Paused.Task.IsCompleted,
                    jobs,
                });
            });
            app.MapPost("/__test/shutdown", (HttpContext http) =>
            {
                if (!Authorized(http)) return Results.NotFound();
                http.Response.OnCompleted(() =>
                {
                    app.Lifetime.StopApplication();
                    return Task.CompletedTask;
                });
                return Results.Ok();
            });
        }

        /// <summary>Append one fsynced JSONL record to the caller-owned state dir.</summary>
        private void WriteLedger(string name, object record)
        {
            var bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(record) + "\n");
            lock (_ledgerLock)
            {
                using var stream = new FileStream(Path.Combine(_dir, name),
                    FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            try
            {
                foreach (var host in _hosts)
                {
                    try { await host.DisposeAsync(); }
                    catch (Exception ex) { failures.Add(ex); }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AI_ENABLED", _oldAiEnabled);
                Environment.SetEnvironmentVariable("ENCRYPTION_KEY", _oldKey);
                try
                {
                    if (_ownsDirectory && Directory.Exists(_dir))
                        Directory.Delete(_dir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add(ex);
                }
            }
            if (failures.Count > 0)
                throw new AggregateException("The HTTP fixture could not release all owned resources.", failures);
        }
    }
}
