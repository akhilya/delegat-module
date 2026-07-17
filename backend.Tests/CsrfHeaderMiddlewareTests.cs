using Delegat.Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public class CsrfHeaderMiddlewareTests
{
    [Fact]
    public async Task Post_ToPublicApiV1_WithoutCsrf_Passes()
    {
        var called = false;
        var middleware = new CsrfHeaderMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/stores/1/products";

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.NotEqual(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task Post_ToSpaApi_WithoutCsrf_Returns403()
    {
        var called = false;
        var middleware = new CsrfHeaderMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/products";

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(403, context.Response.StatusCode);
    }
}
