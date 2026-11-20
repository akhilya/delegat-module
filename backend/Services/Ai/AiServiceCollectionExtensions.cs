using Delegat.Services.Ai.Agent;
using Delegat.Services.Marketplace;
using Delegat.Services.PlatformLink;
using System.Net;

namespace Delegat.Services.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiAssistant(this IServiceCollection services)
    {
        services.AddHttpClient<AiApiClient>(c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddTransient<IAiApiClient>(sp => sp.GetRequiredService<AiApiClient>());
        // IChatClient adapter for the default MAF engine; shares AiApiClient's
        // endpoint/credential/proxy/hosted-signer construction.
        services.AddTransient<IAiRuntimeClientFactory>(sp => sp.GetRequiredService<AiApiClient>());
        services.AddTransient<PlatformLinkHostedAiSigningHandler>();
        services.AddHttpClient("platform-link-hosted-ai", c => c.Timeout = TimeSpan.FromMinutes(5))
            .AddHttpMessageHandler<PlatformLinkHostedAiSigningHandler>();
        // No redirects: the sandbox address is an explicit trusted candidate;
        // a redirect would silently retarget script execution.
        services.AddHttpClient(nameof(PythonSandboxClient), c => c.Timeout = TimeSpan.FromMinutes(2))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            });
        services.AddHttpClient<MarketplaceCredentialBroker>(c => c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
        services.AddHttpClient<McpHttpClient>(c => c.Timeout = TimeSpan.FromSeconds(45));
        services.AddScoped<AiMcpServerService>();
        services.AddScoped<IMcpToolGateway, McpToolGateway>();
        services.AddScoped<AiModelConfigService>();
        services.AddSingleton<AiLiveTurnHub>();
        services.AddScoped<AiDurableTurnStore>();
        services.AddScoped<IAiDurableTurnProducer, AiDurableTurnProducer>();
        services.AddHostedService<AiDurableTurnWorker>();
        services.AddScoped<AiChatService>();
        // Default chat runtime; explicit legacy rollback and pinned older jobs remain supported.
        services.AddScoped<Agent.Maf.MafChatEngine>();
        services.AddSingleton<CabinetCapabilityIndex>();
        services.AddSingleton<CabinetActionCatalog>();
        services.AddScoped<CatalogSearchService>();
        services.AddScoped<SqlReadonlyService>();
        services.AddScoped<KnowledgeIndexService>();
        services.AddSingleton<KnowledgeIndexSnapshotCache>();
        services.AddHttpClient<IKnowledgeEmbeddingClient, KnowledgeEmbeddingClient>(c =>
            c.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            });
        services.AddScoped<AiMemoryService>();
        services.AddSingleton<CabinetPipeline>();
        services.AddScoped<ICabinetRequestInvoker, CabinetRequestInvoker>();
        services.AddScoped<AgentPlatformGateway>();
        services.AddScoped<AgentFileUploadService>();
        services.AddSingleton<AgentFileDownloadTicketService>();
        services.AddScoped<AgentFileDownloadService>();
        services.AddScoped<ChatAttachmentReadService>();
        services.AddScoped<PythonSandboxClient>();
        services.AddScoped<GeneratedArtifactService>();
        services.AddScoped<AgentToolHost>();
        services.AddScoped<AgentSessionResolver>();
        services.AddHostedService<KnowledgeIndexHostedService>();
        services.AddHostedService<KnowledgeEmbeddingHostedService>();
        services.AddHostedService<ChatAttachmentCleanupHostedService>();
        services.AddHostedService<GeneratedArtifactCleanupHostedService>();
        services.AddScoped<MappingIntelligenceService>();
        services.AddScoped<AiOperationService>();
        services.AddScoped<GenerativeMappingService>();
        return services;
    }
}
