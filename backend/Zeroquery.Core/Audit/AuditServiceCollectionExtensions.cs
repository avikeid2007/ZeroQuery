using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Zeroquery.Core.Audit;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddZeroqueryAudit(this IServiceCollection services)
    {
        services.TryAddSingleton<IWriteAuditStore, FileWriteAuditStore>();
        return services;
    }
}
