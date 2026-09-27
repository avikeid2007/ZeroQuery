using Zeroquery.Core.Introspection;

namespace Zeroquery.Core.ConfigGeneration;

/// <summary>Maps Zeroquery's <see cref="DatabaseProvider"/> to DAB's <c>data-source.database-type</c> values.</summary>
internal static class DabDatabaseType
{
    public static string From(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => "mssql",
        DatabaseProvider.PostgreSql => "postgresql",
        DatabaseProvider.MySql => "mysql",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider for DAB config generation.")
    };
}
