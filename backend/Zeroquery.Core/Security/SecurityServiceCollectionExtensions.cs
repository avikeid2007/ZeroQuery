using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zeroquery.Core.ProcessManagement.ResourceLimits;
using Zeroquery.Core.Security.DataProtection;
using Zeroquery.Core.Security.Ssrf;

namespace Zeroquery.Core.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers Phase 6 security services: SSRF validator, connection string protector, and process resource limiter.
    /// </summary>
    public static IServiceCollection AddZeroquerySecurity(
        this IServiceCollection services,
        Action<SsrfOptions>? configureSsrf = null)
    {
        services.AddOptions<SsrfOptions>();
        if (configureSsrf is not null)
        {
            services.Configure(configureSsrf);
        }

        services.TryAddSingleton<ISsrfValidator, SsrfValidator>();
        services.TryAddSingleton<IConnectionStringProtector, DataProtectionConnectionStringProtector>();
        services.TryAddSingleton<IProcessResourceLimiter, ProcessResourceLimiter>();

        return services;
    }
}
