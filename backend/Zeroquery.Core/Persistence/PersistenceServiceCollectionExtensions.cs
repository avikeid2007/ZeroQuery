using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Zeroquery.Core.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers Phase 7 persistence services (saved connections and settings store).
    /// </summary>
    public static IServiceCollection AddZeroqueryPersistence(
        this IServiceCollection services,
        Action<PersistenceOptions>? configure = null)
    {
        services.AddOptions<PersistenceOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<ISavedConnectionStore, FileSavedConnectionStore>();

        return services;
    }
}
