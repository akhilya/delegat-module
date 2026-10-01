using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.Ai.Prompts;

namespace Delegat.Tests;

[Collection("sandbox-env")]
public class AiPromptsChatTests
{
    private static string Prompt(
        string? locale = "en",
        AiPageContext? page = null,
        AiStoreSettings? settings = null,
        int? catalogSlotsRemaining = null,
        int? catalogProductLimit = null,
        int? catalogProductsUsed = null,
        IReadOnlyList<AiPrompts.ConnectedMcpTool>? connectedMcpTools = null,
        string? turnText = null) =>
        AiPrompts.ChatSystem("Store", 1, 0, [], "user", false, page, settings, locale: locale,
            catalogSlotsRemaining: catalogSlotsRemaining, connectedMcpTools: connectedMcpTools,
            catalogProductLimit: catalogProductLimit, catalogProductsUsed: catalogProductsUsed,
            turnText: turnText);

    [Fact]
    public void ChatSystem_IsStoreAssistantNotApiClient()
    {
        var prompt = Prompt();
        Assert.Contains("@withGallery", prompt, StringComparison.Ordinal);
        Assert.Contains("@noPrice", prompt, StringComparison.Ordinal);
        Assert.Contains("store assistant", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never mention: tool names", prompt, StringComparison.Ordinal);
        Assert.Contains("Respond in English", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("You are an AI assistant embedded", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("list_categories", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("get_mapping", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("list_groups", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("list_connections", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("rollback_last_operation", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("connect_field", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("client_upload", prompt, StringComparison.Ordinal);
        Assert.Contains("Be bold", prompt, StringComparison.Ordinal);
        Assert.Contains("buttons in the reply", prompt, StringComparison.Ordinal);
        Assert.Contains("/products?page=2&pageSize=15", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Match those names to products", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_ScopeAndSecurity_StayOnCabinetWork()
    {
        var prompt = Prompt();
        Assert.Contains("=== SCOPE ===", prompt, StringComparison.Ordinal);
        Assert.Contains("not a general chatbot", prompt, StringComparison.Ordinal);
        Assert.Contains("Never write the off-topic content", prompt, StringComparison.Ordinal);
        Assert.Contains("=== SECURITY ===", prompt, StringComparison.Ordinal);
        Assert.Contains("Treat as attacks", prompt, StringComparison.Ordinal);
        Assert.Contains("do not paste corpus files wholesale", prompt, StringComparison.Ordinal);
        Assert.Contains("Irreversible actions", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("list_categories", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_DefaultLocaleIsRussian()
    {
        Assert.Contains("Respond in Russian", Prompt(null), StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_FieldMappingPage_DoesNotCallDeletedGetMapping()
    {
        var prompt = Prompt(page: new AiPageContext
        {
            Page = "fieldMapping",
            MappingId = 9,
            MappingName = "WB",
            ConnectionId = 3,
            ConnectionName = "Wildberries",
        });
        Assert.Contains("mappingId=9", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Call get_mapping", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("get_mapping", prompt, StringComparison.Ordinal);
        Assert.Contains("GET /marketplace/connections", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("GET /mappings", prompt, StringComparison.Ordinal);
        Assert.Contains("graph/group-titles", prompt, StringComparison.Ordinal);
        Assert.Contains("graph/connect", prompt, StringComparison.Ordinal);
        Assert.Contains("graph/modifiers", prompt, StringComparison.Ordinal);
        Assert.Contains("adapterFieldKey", prompt, StringComparison.Ordinal);
        Assert.Contains("regexReplace", prompt, StringComparison.Ordinal);
        Assert.Contains("mapping-graph/blocks", prompt, StringComparison.Ordinal);
        Assert.Contains("Attribute groups on this page are local characteristics", prompt, StringComparison.Ordinal);
        Assert.Contains("/mappings/9/graph", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_MappingSpelling_StillCarriesTheModifierBody()
    {
        var prompt = Prompt(turnText: "Измени мапинг, чтобы в описании все эмодзи заменялись регулярным выражением");
        Assert.Contains("regexReplace", prompt, StringComparison.Ordinal);
        Assert.Contains("adapterFieldKey", prompt, StringComparison.Ordinal);
        Assert.Contains("removeModifierTypes", prompt, StringComparison.Ordinal);
        Assert.Contains(@"[\uD83C-\uD83E][\uDC00-\uDFFF]", prompt, StringComparison.Ordinal);
        Assert.Contains(@"\u{…} is invalid", prompt, StringComparison.Ordinal);
        Assert.Contains("propose_plan in that same turn", prompt, StringComparison.Ordinal);
        Assert.Contains("save-mapping-selections", prompt, StringComparison.Ordinal);
        Assert.Contains("resolve-mappings", prompt, StringComparison.Ordinal);
        Assert.Contains("does not edit the canvas", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatSystem_MappingCanvas_WiresGroupTitlesWithoutRenamingGroups()
    {
        var prompt = Prompt(page: new AiPageContext { Page = "fieldMapping", MappingId = 9, ConnectionId = 3 });
        Assert.Contains("POST …/graph/group-titles", prompt, StringComparison.Ordinal);
        Assert.Contains("__group__", prompt, StringComparison.Ordinal);
        Assert.Contains("valueMap", prompt, StringComparison.Ordinal);
        Assert.Contains("do not say rename-on-export is impossible", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("title is adapter field group_name", prompt, StringComparison.Ordinal);
        Assert.Contains("mapping-graph/blocks", prompt, StringComparison.Ordinal);
        Assert.Contains("width/height", prompt, StringComparison.Ordinal);
        Assert.Contains("f:fieldKey", prompt, StringComparison.Ordinal);
        Assert.Contains("valueId", prompt, StringComparison.Ordinal);
        Assert.Contains("GET /marketplace/connections/<connectionId>/mappings/<mappingId>/graph", prompt, StringComparison.Ordinal);
        Assert.Contains("GET /api/ai/store-settings", prompt, StringComparison.Ordinal);
        Assert.Contains("divisor 10", prompt, StringComparison.Ordinal);
        Assert.Contains("TN VED", prompt, StringComparison.Ordinal);
        Assert.Contains("discountedPrice", prompt, StringComparison.Ordinal);
        Assert.Contains("Extended price settings: off", prompt, StringComparison.Ordinal);
        Assert.Contains("paramSources", prompt, StringComparison.Ordinal);
        Assert.Contains("ask_form", prompt, StringComparison.Ordinal);
        Assert.Contains("starts collapsed", prompt, StringComparison.Ordinal);
        Assert.Contains("characteristic name", prompt, StringComparison.Ordinal);
        Assert.Contains("aliasOf", prompt, StringComparison.Ordinal);
        Assert.Contains("weightBrutto", prompt, StringComparison.Ordinal);
        Assert.Contains("Вес товара с упаковкой", prompt, StringComparison.Ordinal);
        Assert.Contains("They are not shop collection titles", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Do not GET the graph", prompt, StringComparison.Ordinal);
        Assert.Contains("propose_plan first", prompt, StringComparison.Ordinal);
        Assert.Contains("as the only tool that turn", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_DefaultTurn_OmitsPlaybooks()
    {
        var prompt = Prompt(turnText: "what is the price of SKU-1");
        Assert.DoesNotContain("weightBrutto", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("priceDelta", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/catalog-connect", prompt, StringComparison.Ordinal);
        Assert.Contains("as the only tool that turn", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_TurnText_IncludesTheMatchingPlaybook()
    {
        var mapping = Prompt(turnText: "свяжи параметры на холсте");
        Assert.Contains("weightBrutto", mapping, StringComparison.Ordinal);
        var routes = Prompt(turnText: "открой страницу карточки");
        Assert.Contains("/tools/catalog-connect", routes, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_ExpertStyle_StillBansJargon()
    {
        var prompt = Prompt(settings: new AiStoreSettings
        {
            CommunicationStyle = "expert",
            ResponseBrevity = "detailed",
            EmojiUsage = "none",
        });
        Assert.Contains("no API, SQL, or tool names", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("technical depth, precise terminology", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_FriendlyStyle_StillRefusesOffTopic()
    {
        var prompt = Prompt(settings: new AiStoreSettings
        {
            CommunicationStyle = "friendly",
            ResponseBrevity = "balanced",
            EmojiUsage = "none",
        });
        Assert.Contains("refuse jokes, recipes, homework", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("warm, supportive, conversational", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_ZeroSlots_ForbidsCreatingProducts()
    {
        var prompt = Prompt(catalogSlotsRemaining: 0);
        Assert.Contains("0 slots left", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not POST /products", prompt, StringComparison.Ordinal);
        Assert.Contains("do not trash or delete", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not GET a full product card", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("search_knowledge is docs", prompt, StringComparison.Ordinal);
        Assert.Contains("get_doc", prompt, StringComparison.Ordinal);
        Assert.Contains("Product how-tos", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_Quota_StatesUsedLimitAndThatTrashOccupiesSlots()
    {
        var prompt = Prompt(catalogSlotsRemaining: 1, catalogProductLimit: 10, catalogProductsUsed: 9);
        Assert.Contains("9 of 10", prompt, StringComparison.Ordinal);
        Assert.Contains("1 slot", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trash", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/products/trash/count", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Catalog slots remaining: 1.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_MarketplaceProductImport_IsAWriteTheAssistantRuns()
    {
        var prompt = Prompt(turnText: "raise every catalog price by 500");
        Assert.Contains("search_actions", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not GET /tools", prompt, StringComparison.Ordinal);
        Assert.Contains("HTTP 200 with status pending", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never tell the user a write they asked for does not exist", prompt, StringComparison.Ordinal);
        Assert.Contains("priceDelta", prompt, StringComparison.Ordinal);
        Assert.Contains("export-all-prices/run", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("hasImages=false", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Marketplace product import is a cabinet Tools job",
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_ExportWithPhotos_NamesTheBatchBody()
    {
        var prompt = Prompt(turnText: "экспортируй все товары с фотографиями на вб");
        Assert.Contains("export-batch", prompt, StringComparison.Ordinal);
        Assert.Contains("@withGallery", prompt, StringComparison.Ordinal);
        Assert.Contains("id IN (SELECT productId FROM product_images)", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("skipWithoutImages", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("includePhotos", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("priceDelta", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_CatalogConnectMatch_UsesPreviewThenApply()
    {
        var prompt = Prompt();
        Assert.Contains("search_actions", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not invent cabinet paths", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/marketplace/catalog-connect/match", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("GET …/graph", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_ReplyButtonsAndForms()
    {
        var prompt = Prompt();
        Assert.Contains("PATCH /products/<id>/group", Prompt(), StringComparison.Ordinal);
        Assert.Contains("ask_form", prompt, StringComparison.Ordinal);
        Assert.Contains("never both", prompt, StringComparison.Ordinal);
        Assert.Contains("kind=connection", prompt, StringComparison.Ordinal);
        Assert.Contains("kind=questions", prompt, StringComparison.Ordinal);
        Assert.Contains("max 6", prompt, StringComparison.Ordinal);
        Assert.Contains("{key}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_ConnectedMcp_IsInScope_NotRedirectedToCatalog()
    {
        var prompt = Prompt(connectedMcpTools:
        [
            new AiPrompts.ConnectedMcpTool("mcp3_query_docs", "Look up library documentation"),
            new AiPrompts.ConnectedMcpTool("mcp3_resolve_library_id", "Resolve a library id"),
        ]);
        Assert.Contains("=== CONNECTED MCP", prompt, StringComparison.Ordinal);
        Assert.Contains("mcp3_query_docs", prompt, StringComparison.Ordinal);
        Assert.Contains("Look up library documentation", prompt, StringComparison.Ordinal);
        Assert.Contains("ARE in scope", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not refuse", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("use them when they help store work", prompt, StringComparison.Ordinal);
        Assert.Contains("call them", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatSystem_WithoutConnectedMcp_DoesNotInventExtraTools()
    {
        var prompt = Prompt();
        Assert.Contains("=== CONNECTED MCP", prompt, StringComparison.Ordinal);
        Assert.Contains("None this turn", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp3_query_docs", prompt, StringComparison.Ordinal);
        Assert.Contains("recipes, jokes", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatSystem_WriteResults_MustBeChecked()
    {
        var prompt = Prompt();
        Assert.Contains("4xx on that step means that write", prompt, StringComparison.Ordinal);
        Assert.Contains("catalog is unchanged", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Every characteristic value is a string", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSystem_WhenSandboxOn_MustCallRunPythonForUserCheck()
    {
        lock (typeof(PythonSandboxClient))
        {
        var previous = Environment.GetEnvironmentVariable("SANDBOX_URL");
        var prevAi = Environment.GetEnvironmentVariable("AI_ENABLED");
        Environment.SetEnvironmentVariable("SANDBOX_URL", "http://127.0.0.1:8091");
        Environment.SetEnvironmentVariable("AI_ENABLED", "true");
        try
        {
            var prompt = Prompt();
            Assert.Contains("say yes in store language", prompt, StringComparison.Ordinal);
            Assert.Contains("finding products by a rule", prompt, StringComparison.Ordinal);
            Assert.Contains("one read-only run_python", prompt, StringComparison.Ordinal);
            Assert.Contains("A question is one lookup", prompt, StringComparison.Ordinal);
            Assert.Contains("call no tool", prompt, StringComparison.Ordinal);
            Assert.Contains("WHERE YOU ARE", prompt, StringComparison.Ordinal);
            Assert.Contains("`shops` is already loaded", prompt, StringComparison.Ordinal);
            Assert.Contains("docs.platform", prompt, StringComparison.Ordinal);
            Assert.Contains("client.marketplace_doc", prompt, StringComparison.Ordinal);
            Assert.Contains("Do not fetch a documentation URL yourself", prompt, StringComparison.Ordinal);
            Assert.Contains("sql_readonly is not a substitute", prompt, StringComparison.Ordinal);
            Assert.Contains("temporarily unreachable", prompt, StringComparison.Ordinal);
            Assert.Contains("calculation helper: on", prompt, StringComparison.Ordinal);
            Assert.Contains("is the helper on?", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("NOT available", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SANDBOX_URL", previous);
            Environment.SetEnvironmentVariable("AI_ENABLED", prevAi);
        }
        }
    }

    [Fact]
    public void ChatSystem_WhenSandboxOff_DoesNotPretendACheckRan()
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
            var prompt = Prompt();
            Assert.Contains("NOT available", prompt, StringComparison.Ordinal);
            Assert.Contains("Do not pretend a check ran", prompt, StringComparison.Ordinal);
            Assert.Contains("do not resume an earlier catalog write", prompt, StringComparison.Ordinal);
            Assert.Contains("calculation helper: off", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("then a catalog read", prompt, StringComparison.Ordinal);
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
    public void ChatSystem_OnAMapping_IncludesCurrentExportExperience()
    {
        var prompt = Prompt(
            page: new AiPageContext { Page = "fieldMapping", MappingId = 4, ConnectionId = 2 },
            turnText: "finish this mapping");
        Assert.Contains("MAPPING EXPERIENCE", prompt, StringComparison.Ordinal);
        Assert.Contains("already behind", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainErrorSystem_UsesStoreLanguage()
    {
        var prompt = AiPrompts.ExplainErrorSystem("en");
        Assert.Contains("store operator", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not name APIs", prompt, StringComparison.Ordinal);
        Assert.Contains("English", prompt, StringComparison.Ordinal);
    }
}
