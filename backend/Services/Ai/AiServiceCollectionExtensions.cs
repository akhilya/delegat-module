using Delegat.Services.Ai.Agent;
using Delegat.Services.Marketplace;

namespace Delegat.Services.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiAssistant(this IServiceCollection services)
    {
        services.AddHttpClient<AiApiClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient(nameof(PythonSandboxClient), c => c.Timeout = TimeSpan.FromMinutes(2));
        services.AddHttpClient<McpHttpClient>(c => c.Timeout = TimeSpan.FromSeconds(45));
        services.AddScoped<AiMcpServerService>();
        services.AddScoped<IMcpToolGateway, McpToolGateway>();
        services.AddScoped<AiModelConfigService>();
        services.AddSingleton<AiLiveTurnHub>();
        services.AddScoped<AiChatService>();
        services.AddScoped<CatalogSearchService>();
        services.AddScoped<SqlReadonlyService>();
        services.AddScoped<KnowledgeIndexService>();
        services.AddSingleton<CabinetPipeline>();
        services.AddScoped<ICabinetRequestInvoker, CabinetRequestInvoker>();
        services.AddScoped<AgentPlatformGateway>();
        services.AddScoped<AgentFileUploadService>();
        services.AddScoped<PythonSandboxClient>();
        services.AddScoped<AgentToolHost>();
        services.AddScoped<AgentSessionResolver>();
        services.AddHostedService<KnowledgeIndexHostedService>();
        services.AddHostedService<ChatAttachmentCleanupHostedService>();
        services.AddScoped<GraphMutationService>();
        services.AddScoped<MappingIntelligenceService>();
        services.AddScoped<AiOperationService>();
        services.AddScoped<GenerativeMappingService>();
        return services;
    }
}
