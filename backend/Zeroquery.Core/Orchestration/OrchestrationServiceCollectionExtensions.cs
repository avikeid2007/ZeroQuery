using Microsoft.Extensions.DependencyInjection;
using Zeroquery.Core.Mcp;

namespace Zeroquery.Core.Orchestration;

/// <summary>DI registration for the orchestration pipeline (LLM provider + tool-calling loop).</summary>
public static class OrchestrationServiceCollectionExtensions
{
    public static IServiceCollection AddZeroqueryOrchestration(
        this IServiceCollection services,
        Action<OpenRouterOptions>? configureOpenRouter = null,
        Action<OrchestrationOptions>? configureOrchestration = null)
    {
        services.AddOptions<OpenRouterOptions>();
        if (configureOpenRouter is not null)
        {
            services.Configure(configureOpenRouter);
        }

        services.AddOptions<OrchestrationOptions>();
        if (configureOrchestration is not null)
        {
            services.Configure(configureOrchestration);
        }

        services.AddSingleton<ILlmSettingsStore, LlmSettingsStore>();
        services.AddHttpClient<ILlmProvider, OpenRouterLlmProvider>();
        services.AddSingleton<IMcpClientFactory, McpClientFactory>();
        services.AddScoped<OrchestrationService>();
        return services;
    }
}
