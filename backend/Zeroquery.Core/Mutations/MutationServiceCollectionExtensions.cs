using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Zeroquery.Core.Mutations;

public static class MutationServiceCollectionExtensions
{
    public static IServiceCollection AddZeroqueryMutations(this IServiceCollection services)
    {
        services.TryAddSingleton<IMutationService, MutationService>();
        return services;
    }
}
