using Microsoft.Extensions.DependencyInjection;

namespace Zeroquery.Core.Introspection;

/// <summary>
/// DI registration for the schema introspection service and its per-provider implementations.
/// </summary>
public static class IntrospectionServiceCollectionExtensions
{
    public static IServiceCollection AddSchemaIntrospection(this IServiceCollection services)
    {
        services.AddSingleton<ISchemaIntrospector, SqlServerSchemaIntrospector>();
        services.AddSingleton<ISchemaIntrospector, PostgreSqlSchemaIntrospector>();
        services.AddSingleton<ISchemaIntrospector, MySqlSchemaIntrospector>();
        services.AddSingleton<SchemaIntrospectorFactory>();
        return services;
    }
}
