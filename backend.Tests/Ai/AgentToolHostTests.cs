using System.Text;
using System.Text.Json;
using Delegat;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Ai;
using Delegat.Services;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Prompts;
using Delegat.Services.Docs;
using Delegat.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

[Collection("sandbox-env")]
public class AgentToolHostTests
{
    static readonly SemaphoreSlim SandboxEnv = new(1, 1);

    [Fact]
    public async Task DispatchAsync_McpPrefixedTool_UsesGateway()
    {
        await using var db = TestDbFactory.CreateDb();
        var mcp = new FakeMcpGateway();
        var host = CreateHost(db, mcp: mcp);

        var (result, summary) = await host.DispatchAsync(
            "mcp4_get_weather", """{"location":"NYC"}""", 1, 1, default, allowConnectedMcp: true);

        Assert.Equal("mcp4_get_weather", mcp.LastName);
        Assert.Contains("NYC", mcp.LastInput);
        Assert.Contains("ok", JsonSerializer.Serialize(result));
        Assert.Equal("weather", summary);
    }

    [Fact]
    public async Task ConnectedMcp_IsUnavailableOutsideCabinetChat()
    {
        await using var db = TestDbFactory.CreateDb();
        var mcp = new FakeMcpGateway();
        var host = CreateHost(db, mcp: mcp);

        var (result, summary) = await host.DispatchAsync(
            McpToolNames.ForLlm(3, "write_record"), "{}", 1, 1, default);

        Assert.Null(mcp.LastName);
        Assert.Contains("not available on this surface", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connected_tool_not_available", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceView_CannotCallConnectedMcpOrWriteMemories()
    {
        await using var db = TestDbFactory.CreateDb();
        db.aiMemories.Add(new AiMemory
        {
            Id = 1,
            userId = 1,
            storeId = 1,
            category = "preference",
            key = "tone",
            value = "Existing",
        });
        await db.SaveChangesAsync();
        var mcp = new FakeMcpGateway();
        var host = CreateHost(db, permissions: new ViewOnlyPermissions(), mcp: mcp);

        var (mcpResult, mcpSummary) = await host.DispatchAsync(
            McpToolNames.ForLlm(3, "write_record"), "{}", 1, 1, default, allowConnectedMcp: true);
        var (saveResult, saveSummary) = await host.DispatchAsync(
            "memory_save", "{\"category\":\"preference\",\"key\":\"tone\",\"value\":\"Changed\"}", 1, 1, default);
        var (deleteResult, deleteSummary) = await host.DispatchAsync(
            "memory_delete", "{\"id\":1}", 1, 1, default);

        Assert.Null(mcp.LastName);
        Assert.Contains("ai.use required", mcpSummary, StringComparison.Ordinal);
        Assert.Contains("ai.use required", JsonSerializer.Serialize(mcpResult), StringComparison.Ordinal);
        Assert.Contains("read-only", saveSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read_only", JsonSerializer.Serialize(saveResult), StringComparison.Ordinal);
        Assert.Contains("read-only", deleteSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read_only", JsonSerializer.Serialize(deleteResult), StringComparison.Ordinal);
        var stored = Assert.Single(db.aiMemories);
        Assert.Equal("Existing", stored.value);
    }

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
    [InlineData("""{"url":"/products?page=2&pageSize=15"}""", true)]
    [InlineData("""{"url":"/products?trash=1&search=sku-1"}""", true)]
    [InlineData("""{"url":"/products?search=hello%20soap"}""", true)]
    [InlineData("""{"url":"javascript:alert(1)"}""", false)]
    [InlineData("""{"url":"/products?next=https://evil.example"}""", false)]
    public void NavigateTo_AcceptsOnlySafePaths(string input, bool expected)
    {
        var (result, _) = AgentToolHost.NavigateTo(input);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(expected, json.RootElement.GetProperty("navigate").GetBoolean());
    }

    [Fact]
    public void NavigateTo_KeepsCatalogQueryString()
    {
        var (result, _) = AgentToolHost.NavigateTo(
            """{"url":"/products?page=2&pageSize=15","label":"Catalog"}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(json.RootElement.GetProperty("navigate").GetBoolean());
        Assert.Equal("/products?page=2&pageSize=15", json.RootElement.GetProperty("url").GetString());
        Assert.Equal(
            "/products?page=2&pageSize=15",
            json.RootElement.GetProperty("actions")[0].GetProperty("url").GetString());
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
    public void NavigateTo_UrlDropsPromptOnSameButton()
    {
        var (result, _) = AgentToolHost.NavigateTo(
            """{"url":"/products/12/edit","label":"Hello soap","prompt":"hidden follow-up"}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var action = json.RootElement.GetProperty("actions")[0];
        Assert.Equal("open", action.GetProperty("kind").GetString());
        Assert.Equal("/products/12/edit", action.GetProperty("url").GetString());
        Assert.False(action.TryGetProperty("prompt", out _));
    }

    [Fact]
    public void NavigateTo_CapsAtSixButtons()
    {
        var items = string.Join(",", Enumerable.Range(1, 8).Select(i =>
            $$"""{"url":"/products/{{i}}/edit","label":"P{{i}}"}"""));
        var (result, _) = AgentToolHost.NavigateTo($$"""{"items":[{{items}}]}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(6, json.RootElement.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public void AskForm_Connection_RequiresAdapterKey()
    {
        var (bad, _) = AgentToolHost.AskForm("""{"kind":"connection"}""");
        using var badJson = JsonDocument.Parse(JsonSerializer.Serialize(bad));
        Assert.False(badJson.RootElement.GetProperty("form").GetBoolean());

        var (ok, summary) = AgentToolHost.AskForm(
            """{"kind":"connection","adapterKey":"ozon","name":"Shop","title":"Connect Ozon"}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok));
        Assert.True(json.RootElement.GetProperty("form").GetBoolean());
        Assert.Equal("connection", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("ozon", json.RootElement.GetProperty("adapterKey").GetString());
        Assert.Equal("Form", summary);
    }

    [Fact]
    public void AskForm_Fields_DropsSecretsAndPasswords()
    {
        var (result, _) = AgentToolHost.AskForm(
            """{"kind":"fields","followUp":"Shop is {shop}","fields":[{"key":"shop","label":"Shop URL","type":"text"},{"key":"apiKey","label":"Key","type":"password"},{"key":"token","label":"Token","secret":true}]}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(json.RootElement.GetProperty("form").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("fields").GetArrayLength());
        Assert.Equal("shop", json.RootElement.GetProperty("fields")[0].GetProperty("key").GetString());
    }

    [Fact]
    public void AskForm_Questions_CapsAtFiveAndDropsSecrets()
    {
        var steps = string.Join(",", Enumerable.Range(1, 7).Select(i =>
            $$"""{"key":"q{{i}}","title":"Q{{i}}","type":"single","choices":[{"value":"a","label":"A"}]}"""));
        var (result, _) = AgentToolHost.AskForm(
            $$"""{"kind":"questions","questions":[{"key":"secret","title":"Key","type":"password"},{{steps}}]}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(json.RootElement.GetProperty("form").GetBoolean());
        Assert.Equal("questions", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal(5, json.RootElement.GetProperty("questions").GetArrayLength());
        Assert.Equal("single", json.RootElement.GetProperty("questions")[0].GetProperty("type").GetString());
    }

    [Fact]
    public void AskForm_Questions_KeepsMultipleTextAndSkip()
    {
        var (result, _) = AgentToolHost.AskForm(
            """{"kind":"questions","followUp":"Use {adapter} then {notes}","questions":[{"key":"adapter","title":"Which marketplace?","type":"single","choices":[{"value":"ozon","label":"Ozon"},{"value":"wb","label":"Wildberries"}]},{"key":"notes","title":"Anything else?","type":"text","required":false}]}""");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.True(json.RootElement.GetProperty("form").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("questions").GetArrayLength());
        Assert.False(json.RootElement.GetProperty("questions")[1].GetProperty("required").GetBoolean());
        Assert.Equal("text", json.RootElement.GetProperty("questions")[1].GetProperty("type").GetString());
    }

    [Fact]
    public void ToolCatalog_IncludesAskForm()
    {
        Assert.Contains(AgentToolCatalog.Build(), t => t.name == "ask_form");
        Assert.Contains(AgentToolCatalog.Build(), t => t.name == "search_actions");
        Assert.Contains(AgentToolCatalog.Build(), t => t.name == "read_chat_attachment");
        Assert.Contains(AgentToolCatalog.Build(), t => t.name == "create_file_download_link");
        Assert.DoesNotContain(AgentToolCatalog.Build(), t => t.name == "match_files");
    }

    [Fact]
    public async Task ReadChatAttachment_IsOnlyEnabledForCabinetChatTurns()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = CreateHost(db);

        var (mcpResult, _) = await host.DispatchAsync(
            "read_chat_attachment", """{"attachmentId":71}""", 1, 1, default);
        var (chatResult, _) = await host.DispatchAsync(
            "read_chat_attachment", """{"attachmentId":71}""", 1, 1, default,
            allowChatAttachments: true,
            chatSessionId: "chat-session");

        Assert.Contains("chat_attachments_unavailable", JsonSerializer.Serialize(mcpResult), StringComparison.Ordinal);
        Assert.Contains("chat_attachment_reader_unavailable", JsonSerializer.Serialize(chatResult), StringComparison.Ordinal);
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
        Assert.Contains("Не удалось завершить", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("ничего не изменилось", ru, StringComparison.Ordinal);
        Assert.Contains("проверьте результат", ru, StringComparison.Ordinal);
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
        lock (typeof(PythonSandboxClient))
        {
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
        var prevInstance = Environment.GetEnvironmentVariable("INSTANCE_ID");
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("SANDBOX_URL", null);
        Environment.SetEnvironmentVariable("STACK_ID", null);
        Environment.SetEnvironmentVariable("INSTANCE_ID", null);
        Environment.SetEnvironmentVariable("AI_ENABLED", "false");
        try
        {
            Assert.DoesNotContain(AgentToolCatalog.Build(), t => t.name == "run_python");
            var prompt = AiPrompts.ChatSystem("Store", 1, 0, [], "user", false);
            Assert.Contains("NOT available", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", previous);
            Environment.SetEnvironmentVariable("STACK_ID", prevStack);
            Environment.SetEnvironmentVariable("INSTANCE_ID", prevInstance);
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
        }
        }
    }

    [Fact]
    public void ToolCatalog_IncludesRunPythonWhenSandboxUrlSet()
    {
        lock (typeof(PythonSandboxClient))
        {
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("SANDBOX_URL", "http://127.0.0.1:8091");
        Environment.SetEnvironmentVariable("AI_ENABLED", "true");
        try
        {
            Assert.Contains(AgentToolCatalog.Build(), t => t.name == "run_python");
            var prompt = AiPrompts.ChatSystem("Store", 1, 0, [], "user", false);
            Assert.Contains("sandbox is on", prompt, StringComparison.Ordinal);
            Assert.Contains("say yes in store language", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", previous);
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
        }
        }
    }

    [Fact]
    public async Task RunPython_WithoutSandboxUrl_ReturnsNotConfigured()
    {
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevStack = Environment.GetEnvironmentVariable("STACK_ID");
        var prevInstance = Environment.GetEnvironmentVariable("INSTANCE_ID");
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        lock (typeof(PythonSandboxClient))
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", null);
            Environment.SetEnvironmentVariable("STACK_ID", null);
            Environment.SetEnvironmentVariable("INSTANCE_ID", null);
            Environment.SetEnvironmentVariable("AI_ENABLED", "false");
        }
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
            Environment.SetEnvironmentVariable("STACK_ID", prevStack);
            Environment.SetEnvironmentVariable("INSTANCE_ID", prevInstance);
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
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
        await SandboxEnv.WaitAsync();
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("SANDBOX_URL", "http://sandbox.test");
        Environment.SetEnvironmentVariable("AI_ENABLED", "true");
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
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            SandboxEnv.Release();
        }
    }

    [Fact]
    public async Task RunPython_NonZeroExitRemainsAFailedResult()
    {
        await SandboxEnv.WaitAsync();
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("SANDBOX_URL", "http://sandbox.test");
        Environment.SetEnvironmentVariable("AI_ENABLED", "true");
        var handler = new CaptureHandler("""{"exitCode":124,"stdout":"partial","stderr":"timeout","error":"timeout"}""");
        try
        {
            await using var db = TestDbFactory.CreateDb();
            var host = CreateHost(db, new CapturingFactory(handler));
            var (result, summary) = await host.DispatchAsync(
                "run_python", """{"code":"print('never finishes')"}""", 1, 1, default);

            Assert.True(AgentToolResult.IsFailure(result, summary));
            Assert.Contains("failed", summary, StringComparison.OrdinalIgnoreCase);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal(124, json.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Equal("timeout", json.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", previous);
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
            SandboxEnv.Release();
        }
    }

    [Fact]
    public async Task GetDoc_ReturnsArticleBody()
    {
        await using var db = TestDbFactory.CreateDb();
        var docs = new StubPublicDocs();
        var host = CreateHost(db, publicDocs: docs);

        var (result, summary) = await host.DispatchAsync(
            "get_doc", """{"slug":"workspace/products","locale":"ru"}""", 1, 1, default);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("workspace/products", json);
        Assert.Contains("Add SKUs", json);
        Assert.Contains("doc workspace/products", summary);
    }

    [Fact]
    public async Task GetDoc_ReadsIndexedInstancePlaybook()
    {
        await using var db = TestDbFactory.CreateDb();
        db.aiKnowledgeChunks.Add(new Delegat.Models.Ai.AiKnowledgeChunk
        {
            sourcePath = "docs/ai/operator-playbook.md",
            title = "Cabinet assistant playbook",
            body = "Export is local catalog to marketplace. export-batch.",
            createdAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var host = CreateHost(db);

        var (result, summary) = await host.DispatchAsync(
            "get_doc", """{"slug":"docs/ai/operator-playbook.md"}""", 1, 1, default);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("export-batch", json);
        Assert.Contains("docs/ai/operator-playbook.md", summary);
    }

    [Fact]
    public async Task GetDoc_RestoresIndexedSectionHeadings()
    {
        await using var db = TestDbFactory.CreateDb();
        const string path = "docs/guide.md";
        db.aiKnowledgeChunks.AddRange(
            new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = path, title = "Guide", body = "# Guide\n\nIntroduction.",
            },
            new Delegat.Models.Ai.AiKnowledgeChunk
            {
                sourcePath = path, title = "Conditions", body = "Another attempt is not guaranteed success.",
            });
        await db.SaveChangesAsync();
        var host = CreateHost(db);

        var (result, _) = await host.DispatchAsync(
            "get_doc", """{"slug":"docs/guide.md"}""", 1, 1, default);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal("# Guide\n\nIntroduction.\n\n## Conditions\n\nAnother attempt is not guaranteed success.",
            json.RootElement.GetProperty("markdown").GetString());
    }

    [Fact]
    public async Task SearchActions_FindsExportWithoutWanderingTools()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = CreateHost(db);
        var (result, summary) = await host.DispatchAsync(
            "search_actions",
            """{"query":"экспорт всех товаров в medusa без фото"}""",
            1, 1, default);
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("export-batch", json, StringComparison.Ordinal);
        Assert.Contains("allMatching", json, StringComparison.Ordinal);
        Assert.Contains("actions", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchActions_RefreshListings_ReturnsActionAndBodyContract()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = CreateHost(db);
        var (result, _) = await host.DispatchAsync(
            "search_actions",
            """{"query":"обновить карточки в магазине","limit":1}""",
            1, 1, default);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("/products/export-batch", json, StringComparison.Ordinal);
        Assert.Contains("productIds", json, StringComparison.Ordinal);
        Assert.DoesNotContain("export-all-prices", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchKnowledge_ReturnsTheMatchingPassageFromStoredChunks()
    {
        await using var db = TestDbFactory.CreateDb();
        db.aiKnowledgeChunks.Add(new AiKnowledgeChunk
        {
            sourcePath = "docs/operations.md",
            title = "Operations",
            body = new string('a', 1_200) + " The reconciliation checkpoint is mandatory.",
        });
        await db.SaveChangesAsync();

        var (result, _) = await CreateHost(db).DispatchAsync(
            "search_knowledge", """{"query":"reconciliation checkpoint"}""", 1, 1, default);

        Assert.Contains("reconciliation checkpoint is mandatory", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDoc_UnknownSlug_ReturnsToolError()
    {
        await using var db = TestDbFactory.CreateDb();
        var host = CreateHost(db, publicDocs: new StubPublicDocs());

        var (result, summary) = await host.DispatchAsync(
            "get_doc", """{"slug":"workspace/missing"}""", 1, 1, default);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("docs_not_found", json);
        Assert.Equal("docs_not_found", summary);
    }

    [Fact]
    public async Task SqlReadonly_ProductsWithoutGalleryPhotos()
    {
        await using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var user = data.AddUser();
        var withPhoto = data.AddProduct(store.Id, "Has photo", sku: "P1");
        var withoutPhoto = data.AddProduct(store.Id, "No photo", sku: "P2");
        db.files.Add(new Delegat.Models.File
        {
            Id = 1,
            productId = withPhoto.Id,
            authorUserId = user.Id,
            filePath = "a.jpg",
            displayName = "a.jpg",
            contentType = "image/jpeg",
            length = 10,
        });
        db.images.Add(new ImageReference { Id = 1, fileId = 1, productId = withPhoto.Id, index = 0 });
        await data.SaveAsync();
        var host = CreateHost(db);

        var (result, _) = await host.DispatchAsync("sql_readonly",
            """{"sql":"SELECT id, sku FROM products WHERE id NOT IN (SELECT productId FROM product_images)"}""",
            store.Id, 1, default);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        Assert.Equal(1, json.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(withoutPhoto.Id, json.RootElement.GetProperty("rows")[0].GetProperty("id").GetInt32());

        var filesNotIn = await host.DispatchAsync("sql_readonly",
            """{"sql":"SELECT id, sku FROM products WHERE id NOT IN (SELECT productId FROM files)"}""",
            store.Id, 1, default);
        using var filesJson = JsonDocument.Parse(JsonSerializer.Serialize(filesNotIn.result));
        Assert.Equal(1, filesJson.RootElement.GetProperty("rows").GetArrayLength());

        for (var i = 0; i < 25; i++)
        {
            db.files.Add(new Delegat.Models.File
            {
                Id = 10 + i,
                productId = withPhoto.Id,
                authorUserId = user.Id,
                filePath = $"j{i}.jpg",
                displayName = $"j{i}.jpg",
                contentType = "image/jpeg",
                length = 10,
            });
        }

        db.files.Add(new Delegat.Models.File
        {
            Id = 99,
            productId = withPhoto.Id,
            authorUserId = user.Id,
            filePath = "x.png",
            displayName = "x.png",
            contentType = "image/png",
            length = 10,
        });
        await db.SaveChangesAsync();

        var distinct = await host.DispatchAsync("sql_readonly",
            """{"sql":"SELECT DISTINCT contentType FROM files LIMIT 20"}""",
            store.Id, 1, default);
        using var distinctJson = JsonDocument.Parse(JsonSerializer.Serialize(distinct.result));
        Assert.Equal(2, distinctJson.RootElement.GetProperty("rows").GetArrayLength());

        var show = await host.DispatchAsync("sql_readonly",
            """{"sql":"SHOW TABLES"}""",
            store.Id, 1, default);
        using var showJson = JsonDocument.Parse(JsonSerializer.Serialize(show.result));
        Assert.True(showJson.RootElement.GetProperty("rows").GetArrayLength() >= 10);
    }

    private static AgentToolHost CreateHost(
        DataContext db,
        IHttpClientFactory? http = null,
        IStorageService? storage = null,
        ICabinetRequestInvoker? invoker = null,
        IPermissionService? permissions = null,
        IMcpToolGateway? mcp = null,
        IPublicDocsClient? publicDocs = null,
        ChatAttachmentReadService? chatAttachments = null)
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
            NullLogger<AgentToolHost>.Instance,
            mcp,
            publicDocs,
            chatAttachments: chatAttachments);
    }

    private sealed class FakeMcpGateway : IMcpToolGateway
    {
        public string? LastName { get; private set; }
        public string? LastInput { get; private set; }
        public Task<IReadOnlyList<AiTool>> ListToolsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AiTool>>([]);
        public Task<(object result, string summary)> CallAsync(string llmName, string inputJson, CancellationToken ct)
        {
            LastName = llmName;
            LastInput = inputJson;
            return Task.FromResult<(object, string)>((new { ok = true }, "weather"));
        }
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

    private sealed class CaptureHandler(string responseJson = """{"exitCode":0,"stdout":"ping\n","stderr":""}""") : HttpMessageHandler
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
                    responseJson,
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class CapturingFactory(CaptureHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubPublicDocs : IPublicDocsClient
    {
        public Task<DocsCatalog> GetCatalogAsync(string locale, CancellationToken ct = default) =>
            Task.FromResult(new DocsCatalog { Locale = locale, Groups = [] });

        public Task<DocsPage> GetPageAsync(string slug, string locale, CancellationToken ct = default)
        {
            if (slug != "workspace/products")
                throw new DocsClientException("docs_not_found", 404, "missing");
            return Task.FromResult(new DocsPage
            {
                Slug = slug,
                Locale = locale,
                Title = "Products",
                Markdown = "# Products\nAdd SKUs.",
            });
        }
    }
}
