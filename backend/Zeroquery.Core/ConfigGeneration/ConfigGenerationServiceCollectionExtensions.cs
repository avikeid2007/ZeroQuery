using Microsoft.Extensions.DependencyInjection;

namespace Zeroquery.Core.ConfigGeneration;

/// <summary>DI registration for the DAB config validator (the generator itself is static/stateless).</summary>
public static class ConfigGenerationServiceCollectionExtensions
{
    public static IServiceCollection AddDabConfigGeneration(this IServiceCollection services)
    {
        // DabConfigValidator parses/compiles the embedded JSON schema once at construction —
        // registering as a singleton avoids re-parsing it on every request.
        services.AddSingleton<DabConfigValidator>();
        services.AddSingleton<DabConfigService>();
        return services;
    }
}
