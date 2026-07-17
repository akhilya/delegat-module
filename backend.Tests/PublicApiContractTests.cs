using System.Text.Json;
using Delegat.Controllers.PublicApi;
using Delegat.Infrastructure.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class PublicApiContractTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Delegat.Tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData(0, 50, 1, 50)]
    [InlineData(-3, -1, 1, 50)]
    [InlineData(2, 500, 2, 200)]
    [InlineData(1, 200, 1, 200)]
    public void Paging_Normalize_ClampsPageAndSize(int page, int pageSize, int expectedPage, int expectedSize)
    {
        var (p, s) = PublicApiPaging.Normalize(page, pageSize);
        Assert.Equal(expectedPage, p);
        Assert.Equal(expectedSize, s);
    }

    [Fact]
    public void Paging_Normalize_OperationsDefaults()
    {
        var (p, s) = PublicApiPaging.Normalize(
            0,
            0,
            PublicApiPaging.DefaultOperationsPageSize,
            PublicApiPaging.MaxOperationsPageSize);
        Assert.Equal(1, p);
        Assert.Equal(20, s);

        (_, s) = PublicApiPaging.Normalize(
            1,
            999,
            PublicApiPaging.DefaultOperationsPageSize,
            PublicApiPaging.MaxOperationsPageSize);
        Assert.Equal(100, s);
    }

    [Fact]
    public void AcceptedOperation_SetsLocationAndBody()
    {
        var result = PublicApiResults.AcceptedOperation("op-1", 9) as AcceptedResult;
        Assert.NotNull(result);
        Assert.Equal("/api/v1/stores/9/operations/op-1", result!.Location);

        var json = JsonSerializer.Serialize(result.Value);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("op-1", doc.RootElement.GetProperty("operationId").GetString());
        Assert.Equal("running", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "/api/v1/stores/9/operations/op-1",
            doc.RootElement.GetProperty("links").GetProperty("self").GetString());
    }

    [Fact]
    public void Page_IncludesItemsAndTotals()
    {
        var page = PublicApiResults.Page(new[] { "a", "b" }, 2, 10, 42);
        var json = JsonSerializer.Serialize(page);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, doc.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(42, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(401, "unauthorized")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")]
    [InlineData(409, "conflict")]
    [InlineData(422, "validation_error")]
    [InlineData(429, "rate_limited")]
    [InlineData(400, "bad_request")]
    public async Task ExceptionMiddleware_PublicApi_UsesErrorEnvelope(int status, string code)
    {
        var middleware = new ExceptionMiddleware(
            _ => throw new ApiException("partner message", status),
            NullLogger<ExceptionMiddleware>.Instance,
            new FakeHostEnvironment());

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/stores/1/products";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(code, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("partner message", doc.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task ExceptionMiddleware_SpaPath_UsesTitleStatusShape()
    {
        var middleware = new ExceptionMiddleware(
            _ => throw ApiException.Forbidden("nope"),
            NullLogger<ExceptionMiddleware>.Instance,
            new FakeHostEnvironment());

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/products";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("nope", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ExceptionMiddleware_PublicApi_Unhandled_HidesDetailsInProduction()
    {
        var middleware = new ExceptionMiddleware(
            _ => throw new InvalidOperationException("secret internals"),
            NullLogger<ExceptionMiddleware>.Instance,
            new FakeHostEnvironment { EnvironmentName = Environments.Production });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/stores/1/sync";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("internal_error", body);
        Assert.DoesNotContain("secret internals", body);
    }
}
