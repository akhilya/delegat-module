using Delegat.Infrastructure.Configuration;

namespace Delegat.Services.Docs;

public static class DocsServiceCollectionExtensions
{
    public static IServiceCollection AddPublicDocs(this IServiceCollection services)
    {
        services.AddSingleton(PublicDocsOptions.FromEnvironment());
        services.AddHttpClient(PublicDocsClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Accept.ParseAdd("text/markdown, application/json, */*");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
        });
        services.AddSingleton<IPublicDocsClient, PublicDocsClient>();
        return services;
    }
}
