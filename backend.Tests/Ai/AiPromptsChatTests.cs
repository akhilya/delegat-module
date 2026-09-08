using Delegat.Models.Ai;
using Delegat.Services.Ai.Prompts;

namespace Delegat.Tests;

public class AiPromptsChatTests
{
    private static string Prompt(
        string? locale = "en",
        AiPageContext? page = null,
        AiStoreSettings? settings = null,
        int? catalogSlotsRemaining = null,
        IReadOnlyList<AiPrompts.ConnectedMcpTool>? connectedMcpTools = null) =>
        AiPrompts.ChatSystem("Store", 1, 0, [], "user", false, page, settings, locale: locale,
            catalogSlotsRemaining: catalogSlotsRemaining, connectedMcpTools: connectedMcpTools);

    [Fact]
    public void ChatSystem_IsStoreAssistantNotApiClient()
    {
        var prompt = Prompt();
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
    public void ExplainErrorSystem_UsesStoreLanguage()
    {
        var prompt = AiPrompts.ExplainErrorSystem("en");
        Assert.Contains("store operator", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not name APIs", prompt, StringComparison.Ordinal);
        Assert.Contains("English", prompt, StringComparison.Ordinal);
    }
}
