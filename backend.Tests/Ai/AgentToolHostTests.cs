using System.Text;
using System.Text.Json;
using Delegat;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Services;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Prompts;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class AgentToolHostTests
{
    [Fact]
    public async Task DispatchAsync_UnknownTool_ReturnsError()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = CreateHost(db);

        var (result, summary) = await host.DispatchAsync("does_not_exist", "{}", 1, 1, default);

        Assert.Contains("Unknown tool: does_not_exist", JsonSerializer.Serialize(result));
        Assert.Contains("Unknown tool", summary);
    }

    [Fact]
    public async Task DispatchAsync_ClientUpload_IsUnknown()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = CreateHost(db);

        var (_, summary) = await host.DispatchAsync("client_upload", "{}", 1, 1, default);

        Assert.Contains("Unknown tool", summary);
    }

    [Theory]
    [InlineData("""{"url":"/products/12","label":"Products"}""", true)]
    [InlineData("""{"url":"javascript:alert(1)"}""", false)]
    public void NavigateTo_AcceptsOnlySafePaths(string input, bool expected)
    {
        var (result, _) = AgentToolHost.NavigateTo(input);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(expected, json.RootElement.GetProperty("navigate").GetBoolean());
    }

    [Fact]
    public void NavigateTo_ItemsAndPromptOnlyButtons()
    {
        var (result, _) = AgentToolHost.NavigateTo(
            """{"items":[{"url":"/products/12/edit","label":"Hello soap"},{"label":"Put photos","prompt":"Put these photos on Hello soap"}]}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(json.RootElement.GetProperty("navigate").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("actions").GetArrayLength());
        Assert.Equal("/products/12/edit", json.RootElement.GetProperty("actions")[0].GetProperty("url").GetString());
        Assert.False(json.RootElement.GetProperty("actions")[1].TryGetProperty("url", out _));
        Assert.Equal("Put these photos on Hello soap", json.RootElement.GetProperty("actions")[1].GetProperty("prompt").GetString());
    }

    [Fact]
    public void NavigateTo_PromptOnly_HasNoUrl()
    {
        var (result, _) = AgentToolHost.NavigateTo(
            """{"label":"Put photos on Hello soap","prompt":"Put these photos on Hello soap"}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(json.RootElement.GetProperty("navigate").GetBoolean());
        Assert.Equal("", json.RootElement.GetProperty("url").GetString());
        Assert.Equal("Put these photos on Hello soap", json.RootElement.GetProperty("actions")[0].GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task PlatformRequest_ProductCap_IsPlanFailureNotSuccess()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = CreateHost(db, invoker: new QuotaInvoker());
        var (result, summary) = await host.DispatchAsync(
            "platform_request",
            """{"method":"POST","path":"/products","body":{"name":"Cap test","sku":"CAP-1","price":1}}""",
            1, 1, default);

        Assert.True(AgentToolResult.IsFailure(result, summary));
        Assert.Contains("nothing was created", summary, StringComparison.OrdinalIgnoreCase);
        var ru = AgentToolResult.FormatPlanOutcome(0, 1, [summary], "ru");
        Assert.Contains("Не получилось", ru, StringComparison.Ordinal);
        Assert.Contains("ничего не изменилось", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("Plan executed", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("Готово: выполнены все шаги", ru, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformRequest_ForwardsAllowedPathToCabinet()
    {
        await using var db = TestDbFactory.CreateDb();
        var invoker = new RecordingInvoker();
        var host = CreateHost(db, invoker: invoker);

        var (result, _) = await host.DispatchAsync("platform_request",
            """{"method":"GET","path":"/products?categoryId=9"}""", 1, 1, default);

        Assert.Equal("GET", invoker.Method);
        Assert.Equal("/api/products", invoker.Path);
        Assert.Equal("9", invoker.Query!["categoryId"]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task PlatformRequest_DeniedRoute_DoesNotInvokeCabinet()
    {
        await using var db = TestDbFactory.CreateDb();
        var invoker = new RecordingInvoker();
        var host = CreateHost(db, invoker: invoker);

        var (result, summary) = await host.DispatchAsync("platform_request",
            """{"method":"DELETE","path":"/stores/1"}""", 1, 1, default);

        Assert.Null(invoker.Path);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("route_denied", json.RootElement.GetProperty("error").GetString());
        Assert.Contains("not allowed", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlatformRequest_ReadOnly_RejectsWrites()
    {
        await using var db = TestDbFactory.CreateDb();
        var invoker = new RecordingInvoker();
        var host = CreateHost(db, invoker: invoker, permissions: new ViewOnlyPermissions());

        var (result, _) = await host.DispatchAsync("platform_request",
            """{"method":"POST","path":"/products","body":{"name":"X"}}""", 1, 1, default);

        Assert.Null(invoker.Path);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("forbidden", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SearchCatalog_FindsProductBySku()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        data.AddProduct(store.Id, "Hello soap", sku: "SKU-HELLO");
        await data.SaveAsync();
        var host = CreateHost(db);

        var (result, _) = await host.DispatchAsync("search_catalog", """{"query":"SKU-HELLO"}""", store.Id, 1, default);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(1, json.RootElement.GetProperty("products").GetArrayLength());
    }

    [Fact]
    public async Task MatchFiles_MapsSkuFilename()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var product = data.AddProduct(store.Id, "Hello soap", sku: "SKU-HELLO");
        await data.SaveAsync();
        var host = CreateHost(db);

        var (result, _) = await host.DispatchAsync("match_files",
            """{"files":[{"relativePath":"photos/SKU-HELLO.jpg","fileName":"SKU-HELLO.jpg"}]}""",
            store.Id, 1, default);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var match = json.RootElement.GetProperty("matches")[0];
        Assert.Equal(product.Id, match.GetProperty("ProductId").GetInt32());
    }

    [Fact]
    public async Task SqlReadonly_SelectsProductsForStore()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        data.AddProduct(store.Id, "A", sku: "A1");
        await data.SaveAsync();
        var host = CreateHost(db);

        var (result, _) = await host.DispatchAsync("sql_readonly",
            """{"sql":"SELECT id, sku, name FROM products LIMIT 10"}""", store.Id, 1, default);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(1, json.RootElement.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task SqlReadonly_SelectsAttributeRowsAndInList()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var category = data.AddCategory(store.Id, "T-shirts");
        var attr = data.AddCategoryAttribute(category.Id, "Color", key: "color");
        var a = data.AddProduct(store.Id, "A", sku: "A1", categoryId: category.Id);
        var b = data.AddProduct(store.Id, "B", sku: "B1", categoryId: category.Id);
        db.productAttributeValues.Add(new ProductAttributeValue
        {
            productId = a.Id,
            attributeId = attr.Id,
            value = "white",
        });
        await data.SaveAsync();
        var host = CreateHost(db);

        var attrs = await host.DispatchAsync("sql_readonly",
            $$"""{"sql":"SELECT id, name, key FROM category_attributes WHERE categoryId = {{category.Id}}"}""",
            store.Id, 1, default);
        using var attrsJson = JsonDocument.Parse(JsonSerializer.Serialize(attrs.result));
        Assert.Equal(1, attrsJson.RootElement.GetProperty("rows").GetArrayLength());

        var products = await host.DispatchAsync("sql_readonly",
            $$"""{"sql":"SELECT id, sku FROM products WHERE id IN ({{a.Id}}, {{b.Id}})"}""",
            store.Id, 1, default);
        using var productsJson = JsonDocument.Parse(JsonSerializer.Serialize(products.result));
        Assert.Equal(2, productsJson.RootElement.GetProperty("rows").GetArrayLength());

        var counts = await host.DispatchAsync("sql_readonly",
            """{"sql":"SELECT COUNT(*) AS product_count, COUNT(DISTINCT categoryId) AS categories_used FROM products"}""",
            store.Id, 1, default);
        using var countsJson = JsonDocument.Parse(JsonSerializer.Serialize(counts.result));
        Assert.Equal(2, countsJson.RootElement.GetProperty("rows")[0].GetProperty("product_count").GetInt32());
        Assert.Equal(1, countsJson.RootElement.GetProperty("rows")[0].GetProperty("categories_used").GetInt32());
    }

    [Fact]
    public void ToolCatalog_OmitsRunPythonWhenSandboxUrlUnset()
    {
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        Environment.SetEnvironmentVariable("SANDBOX_URL", null);
        try
        {
            Assert.DoesNotContain(AgentToolCatalog.Build(), t => t.name == "run_python");
            var prompt = AiPrompts.ChatSystem("Store", 1, 0, [], "user", false);
            Assert.Contains("NOT available", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", previous);
        }
    }

    [Fact]
    public void ToolCatalog_IncludesRunPythonWhenSandboxUrlSet()
    {
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        Environment.SetEnvironmentVariable("SANDBOX_URL", "http://127.0.0.1:8091");
        try
        {
            Assert.Contains(AgentToolCatalog.Build(), t => t.name == "run_python");
            var prompt = AiPrompts.ChatSystem("Store", 1, 0, [], "user", false);
            Assert.Contains("sandbox is on", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", previous);
        }
    }

    [Fact]
    public async Task RunPython_WithoutSandboxUrl_ReturnsNotConfigured()
    {
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        Environment.SetEnvironmentVariable("SANDBOX_URL", null);
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var host = CreateHost(db);
            var (result, summary) = await host.DispatchAsync("run_python", """{"code":"print(1)"}""", 1, 1, default);
            Assert.Contains("sandbox_not_configured", JsonSerializer.Serialize(result));
            Assert.Contains("not configured", summary);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", previous);
        }
    }

    [Fact]
    public void CreateRunBody_SetsContentLength()
    {
        using var content = PythonSandboxClient.CreateRunBody("print('ping')", 1, "http://x", "Bearer t");
        Assert.True(content.Headers.ContentLength > 0);
        Assert.Equal("application/json", content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RunPython_PostsJsonWithContentLength()
    {
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        Environment.SetEnvironmentVariable("SANDBOX_URL", "http://sandbox.test");
        var handler = new CaptureHandler();
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var host = CreateHost(db, new CapturingFactory(handler));
            var (result, summary) = await host.DispatchAsync(
                "run_python", """{"code":"print('ping')"}""", 1, 1, default);

            Assert.NotNull(handler.Last);
            Assert.True(handler.ContentLength > 0);
            Assert.DoesNotContain("413", summary, StringComparison.Ordinal);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal("ping\n", json.RootElement.GetProperty("stdout").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", previous);
        }
    }

    private static AgentToolHost CreateHost(
        DataContext db,
        IHttpClientFactory? http = null,
        IStorageService? storage = null,
        ICabinetRequestInvoker? invoker = null,
        IPermissionService? permissions = null)
    {
        storage ??= new FakeStorage();
        var catalog = new CatalogSearchService(db);
        var sql = new SqlReadonlyService(db);
        var knowledge = new KnowledgeIndexService(db, storage, NullLogger<KnowledgeIndexService>.Instance);
        var uow = TestDbFactory.CreateUnitOfWork(db);
        var uploads = new AgentFileUploadService(db, uow, storage, new QuotaService(uow, db));
        var platform = new AgentPlatformGateway(uploads, invoker ?? new RecordingInvoker());
        var python = new PythonSandboxClient(
            db,
            http ?? new ThrowingHttpFactory(),
            NullLogger<PythonSandboxClient>.Instance,
            new HttpContextAccessor(),
            new FakeJwt());
        return new AgentToolHost(
            catalog, sql, knowledge, platform, python,
            permissions ?? new GrantAllPermissions(),
            db,
            NullLogger<AgentToolHost>.Instance);
    }

    private sealed class QuotaInvoker : ICabinetRequestInvoker
    {
        public Task<(object result, string summary)> InvokeAsync(
            int storeId,
            int userId,
            string method,
            string apiPath,
            IReadOnlyDictionary<string, string> query,
            JsonElement? body,
            CancellationToken ct)
        {
            var json = """{"title":"Product limit reached (MAX_PRODUCTS=11)","status":409,"code":"product_limit_reached","data":{"limit":11,"used":11,"remaining":0}}""";
            return Task.FromResult(CabinetRequestInvoker.InterpretResponse(409, Encoding.UTF8.GetBytes(json)));
        }
    }

    private sealed class RecordingInvoker : ICabinetRequestInvoker
    {
        public string? Method { get; private set; }
        public string? Path { get; private set; }
        public IReadOnlyDictionary<string, string>? Query { get; private set; }

        public Task<(object result, string summary)> InvokeAsync(
            int storeId,
            int userId,
            string method,
            string apiPath,
            IReadOnlyDictionary<string, string> query,
            JsonElement? body,
            CancellationToken ct)
        {
            Method = method;
            Path = apiPath;
            Query = query;
            return Task.FromResult<(object, string)>((new { ok = true, path = apiPath }, "stub"));
        }
    }

    private sealed class ViewOnlyPermissions : IPermissionService
    {
        public Task EnsureRolesSeededAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task MigrateLegacyAccessAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([Permissions.WorkspaceView]);
        public Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<UserAccessDto> GetUserAccessAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult(new UserAccessDto());
        public Task SetUserAccessAsync(int userId, IReadOnlyList<string> roleNames, IReadOnlyList<string> permissionGrants, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task EnsureOwnerAccessAsync(Delegat.Models.User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task HydrateClaimsAsync(System.Security.Claims.ClaimsIdentity identity, int userId, CancellationToken ct = default) =>
            Task.CompletedTask;
        public void InvalidateUserAccess(int userId) { }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public long? ContentLength { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            ContentLength = request.Content?.Headers.ContentLength;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"exitCode":0,"stdout":"ping\n","stderr":""}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class CapturingFactory(CaptureHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
