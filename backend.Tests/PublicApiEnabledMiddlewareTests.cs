using System.Text.Json;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public class PublicApiEnabledMiddlewareTests
{
    private sealed class StubPublicApiSettings(bool enabled) : IPublicApiSettingsService
    {
        public bool IsPublicApiEnabled { get; } = enabled;
        public Task EnsureInitializedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<PublicApiSettingsDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new PublicApiSettingsDto(IsPublicApiEnabled, DateTime.UtcNow));
        public Task<PublicApiSettingsDto> UpdateAsync(UpdatePublicApiSettingsRequest request, CancellationToken ct = default) =>
            GetAsync(ct);
    }

    [Fact]
    public async Task PublicApiPath_WhenDisabled_Returns503Envelope()
    {
        var called = false;
        var middleware = new PublicApiEnabledMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/stores/1/products";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, new StubPublicApiSettings(enabled: false));

        Assert.False(called);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("PUBLIC_API_DISABLED", error.GetProperty("code").GetString());
        Assert.Equal("Public API is disabled for this instance.", error.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("details").ValueKind);
    }

    [Fact]
    public async Task PublicApiPath_WhenEnabled_PassesThrough()
    {
        var called = false;
        var middleware = new PublicApiEnabledMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/stores/1/products";

        await middleware.InvokeAsync(context, new StubPublicApiSettings(enabled: true));

        Assert.True(called);
        Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [Fact]
    public async Task SpaApiPath_WhenPublicApiDisabled_PassesThrough()
    {
        var called = false;
        var middleware = new PublicApiEnabledMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/products";

        await middleware.InvokeAsync(context, new StubPublicApiSettings(enabled: false));

        Assert.True(called);
        Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }
}
