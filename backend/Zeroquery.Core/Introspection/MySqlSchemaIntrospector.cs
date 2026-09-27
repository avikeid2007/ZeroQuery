using System.Data.Common;
using MySqlConnector;

namespace Zeroquery.Core.Introspection;

/// <summary>
/// Introspects a MySQL database via INFORMATION_SCHEMA views.
/// MySQL has no separate "schema" concept from "database", so every query is scoped to
/// the current database via DATABASE() and <see cref="TableInfo.Schema"/> is always empty.
/// </summary>
public sealed class MySqlSchemaIntrospector : InformationSchemaIntrospectorBase
{
    public override DatabaseProvider Provider => DatabaseProvider.MySql;

    protected override DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    protected override string TablesSql => """
        SELECT '' AS table_schema, TABLE_NAME,
               CASE WHEN TABLE_TYPE = 'VIEW' THEN 1 ELSE 0 END AS IS_VIEW
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY TABLE_NAME
        """;

    protected override string ColumnsSql => """
        SELECT '' AS table_schema, TABLE_NAME, COLUMN_NAME, DATA_TYPE,
               CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IS_NULLABLE,
               CHARACTER_MAXIMUM_LENGTH
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY TABLE_NAME, ORDINAL_POSITION
        """;

    protected override string PrimaryKeysSql => """
        SELECT '' AS table_schema, TABLE_NAME, COLUMN_NAME
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME = 'PRIMARY'
        """;

    protected override string ForeignKeysSql => """
        SELECT CONSTRAINT_NAME,
               '' AS from_schema, TABLE_NAME, COLUMN_NAME,
               '' AS to_schema, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
        WHERE TABLE_SCHEMA = DATABASE()
          AND REFERENCED_TABLE_NAME IS NOT NULL
        """;
}
