namespace Zeroquery.Core.Introspection;

/// <summary>
/// Database engines supported by the schema introspection service.
/// These map 1:1 to the database providers Data API Builder (DAB) supports.
/// </summary>
public enum DatabaseProvider
{
    SqlServer,
    PostgreSql,
    MySql
}
