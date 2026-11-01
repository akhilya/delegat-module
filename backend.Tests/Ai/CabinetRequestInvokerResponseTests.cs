using System.Text.Json;
using Delegat.Infrastructure.Auth;
using Delegat.Services.Ai.Agent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class CabinetRequestInvokerResponseTests
{
    [Theory]
    [InlineData(CabinetRequestInvoker.MaxResponseBytes - 1, false)]
    [InlineData(CabinetRequestInvoker.MaxResponseBytes, false)]
    [InlineData(CabinetRequestInvoker.MaxResponseBytes + 1, true)]
    public async Task NestedJsonResponse_IsBoundedAt256Kb(int responseBytes, bool tooLarge)
    {
        var body = new byte[responseBytes];
        var invoker = CreateInvoker(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(body, default);
        });

        var (result, _) = await invoker.InvokeAsync(1, 1, "GET", "/api/test", EmptyQuery(), null, default);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        if (tooLarge)
        {
            Assert.Equal("response_too_large", json.RootElement.GetProperty("error").GetString());
            Assert.Equal(CabinetRequestInvoker.MaxResponseBytes, json.RootElement.GetProperty("limitBytes").GetInt32());
            Assert.Equal("json", json.RootElement.GetProperty("format").GetString());
        }
        else
        {
            Assert.False(json.RootElement.TryGetProperty("error", out _));
            Assert.Equal(responseBytes, json.RootElement.GetProperty("text").GetString()!.Length);
        }
    }

    [Fact]
    public async Task NestedBinaryResponse_IsRejectedBeforeCapturingAnyBody()
    {
        var attemptedWrites = 0;
        var binary = new byte[8 * 1024 * 1024];
        var invoker = CreateInvoker(async context =>
        {
            context.Response.ContentType = "model/stl";
            context.Response.Headers.ContentDisposition = "attachment; filename=part.stl";
            context.Response.ContentLength = binary.Length;
            attemptedWrites++;
            await context.Response.Body.WriteAsync(binary, default);
        });

        var (result, summary) = await invoker.InvokeAsync(1, 1, "GET", "/api/files/2/download", EmptyQuery(), null, default);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        Assert.Equal(1, attemptedWrites);
        Assert.Equal("binary_response_requires_download", json.RootElement.GetProperty("error").GetString());
        Assert.Contains("create_file_download_link", json.RootElement.GetProperty("hint").GetString());
        Assert.Contains("before buffering", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NestedResponseWrite_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoker = CreateInvoker(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(new byte[] { 1 }, cancellation.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invoker.InvokeAsync(1, 1, "GET", "/api/test", EmptyQuery(), null, cancellation.Token));
    }

    private static CabinetRequestInvoker CreateInvoker(RequestDelegate next)
    {
        var pipeline = new CabinetPipeline();
        pipeline.Capture(next);
        var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new CabinetRequestInvoker(
            pipeline,
            new HttpContextAccessor(),
            new FakeJwt(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CabinetRequestInvoker>.Instance);
    }

    private static IReadOnlyDictionary<string, string> EmptyQuery() =>
        new Dictionary<string, string>();
}
