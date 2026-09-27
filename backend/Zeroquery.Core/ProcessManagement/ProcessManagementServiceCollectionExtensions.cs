using Microsoft.Extensions.DependencyInjection;

namespace Zeroquery.Core.ProcessManagement;

/// <summary>DI registration for the DAB Process Manager and its idle-reaper background service.</summary>
public static class ProcessManagementServiceCollectionExtensions
{
    /// <param name="configure">
    /// Optional callback to override defaults in <see cref="DabProcessManagerOptions"/>
    /// (e.g. bind from configuration in Program.cs: <c>options => builder.Configuration
    /// .GetSection("DabProcessManager").Bind(options)</c>).
    /// </param>
    public static IServiceCollection AddDabProcessManagement(
        this IServiceCollection services,
        Action<DabProcessManagerOptions>? configure = null)
    {
        services.AddOptions<DabProcessManagerOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<DabProcessManager>();
        services.AddHostedService<DabIdleReaperService>();
        return services;
    }
}
