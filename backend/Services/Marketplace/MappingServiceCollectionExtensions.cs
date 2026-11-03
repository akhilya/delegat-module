using Delegat.Services.Ai;

namespace Delegat.Services.Marketplace;

public static class MappingServiceCollectionExtensions
{
    public static IServiceCollection AddMarketplaceMappings(this IServiceCollection services, bool aiEnabled)
    {
        // Shared by the cabinet editor and the assistant; graph edits do not require AI.
        services.AddScoped<GraphMutationService>();
        services.AddScoped<MappingService>(sp => new MappingService(
            sp.GetRequiredService<DataContext>(),
            sp,
            aiEnabled ? sp.GetService<GenerativeMappingService>() : null));
        services.AddScoped<ProductMappingSelectionService>();
        return services;
    }
}
