using System.Data.Common;
using Npgsql;

namespace Zeroquery.Core.Introspection;

/// <summary>
/// Introspects a PostgreSQL database via INFORMATION_SCHEMA views.
/// Excludes the built-in "pg_catalog" and "information_schema" system schemas.
/// </summary>
public sealed class PostgreSqlSchemaIntrospector : InformationSchemaIntrospectorBase
{
    public override DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    protected override DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    protected override string TablesSql => """
        SELECT table_schema, table_name,
               CASE WHEN table_type = 'VIEW' THEN 1 ELSE 0 END AS is_view
        FROM information_schema.tables
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name
        """;

    protected override string ColumnsSql => """
        SELECT table_schema, table_name, column_name, data_type,
               CASE WHEN is_nullable = 'YES' THEN 1 ELSE 0 END AS is_nullable,
               character_maximum_length
        FROM information_schema.columns
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name, ordinal_position
        """;

    protected override string PrimaryKeysSql => """
        SELECT kcu.table_schema, kcu.table_name, kcu.column_name
        FROM information_schema.key_column_usage kcu
        JOIN information_schema.table_constraints tc
            ON kcu.constraint_name = tc.constraint_name
           AND kcu.table_schema = tc.table_schema
        WHERE tc.constraint_type = 'PRIMARY KEY'
        """;

    protected override string ForeignKeysSql => """
        SELECT fk.constraint_name,
               fk_cu.table_schema, fk_cu.table_name, fk_cu.column_name,
               pk_cu.table_schema, pk_cu.table_name, pk_cu.column_name
        FROM information_schema.referential_constraints fk
        JOIN information_schema.key_column_usage fk_cu
            ON fk.constraint_name = fk_cu.constraint_name
           AND fk.constraint_schema = fk_cu.constraint_schema
        JOIN information_schema.key_column_usage pk_cu
            ON fk.unique_constraint_name = pk_cu.constraint_name
           AND fk.unique_constraint_schema = pk_cu.constraint_schema
           AND fk_cu.ordinal_position = pk_cu.ordinal_position
        """;
}
