using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zeroquery.Core.Audit;
using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Mutations;
using Zeroquery.Core.Orchestration;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.ProcessManagement;
using Zeroquery.Core.Security;

namespace ZeroQuery.Desktop.Services;

public static class DesktopServiceContainer
{
    private static IServiceProvider? _serviceProvider;

    public static IServiceProvider Services =>
        _serviceProvider ?? throw new InvalidOperationException("Desktop service container has not been initialized.");

    public static IServiceProvider Initialize()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Data protection (for encrypting connection strings & LLM keys)
        services.AddDataProtection();

        // Phase 6 Security (Local desktop connects to local/private DBs)
        services.AddZeroquerySecurity(options =>
        {
            options.BlockPrivateNetworks = false;
        });

        // Phase 7 Persistence (%LocalAppData%\Zeroquery)
        services.AddZeroqueryPersistence(options =>
        {
            options.Mode = "save";
        });

        // Phase 8 Audit & Mutations
        services.AddZeroqueryAudit();
        services.AddZeroqueryMutations();

        // Phase 1 Schema Introspection (SQL Server, Postgres, MySQL)
        services.AddSchemaIntrospection();

        // Phase 2 Config Generation (DAB draft schema)
        services.AddDabConfigGeneration();

        // Phase 3 Process Management (DAB Child Process runner)
        services.AddDabProcessManagement(options =>
        {
            options.MaxConcurrentInstances = 5;
            options.IdleTimeoutMinutes = 60;
            options.MaxMemoryMegabytes = 1024;
            options.CpuLimitPercent = 80;
        });

        // Phase 4 Orchestration (LLM provider + tool calling loop)
        services.AddZeroqueryOrchestration();

        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }

    public static void Shutdown()
    {
        if (_serviceProvider != null)
        {
            try
            {
                // Graceful cleanup: kill any running child DAB processes
                var processManager = _serviceProvider.GetService<DabProcessManager>();
                processManager?.StopAll();
            }
            catch
            {
                // Suppress on exit
            }

            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _serviceProvider = null;
        }
    }
}
