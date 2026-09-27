using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Zeroquery.Core.Introspection;

/// <summary>
/// Introspects a Microsoft SQL Server (or Azure SQL) database via INFORMATION_SCHEMA views.
/// </summary>
public sealed class SqlServerSchemaIntrospector : InformationSchemaIntrospectorBase
{
    public override DatabaseProvider Provider => DatabaseProvider.SqlServer;

    protected override DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    protected override string TablesSql => """
        SELECT TABLE_SCHEMA, TABLE_NAME,
               CASE WHEN TABLE_TYPE = 'VIEW' THEN 1 ELSE 0 END AS IS_VIEW
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
        ORDER BY TABLE_SCHEMA, TABLE_NAME
        """;

    protected override string ColumnsSql => """
        SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE,
               CASE WHEN IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IS_NULLABLE,
               CHARACTER_MAXIMUM_LENGTH
        FROM INFORMATION_SCHEMA.COLUMNS
        ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
        """;

    protected override string PrimaryKeysSql => """
        SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME
        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
        JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
           AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA
        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
        """;

    protected override string ForeignKeysSql => """
        SELECT fk.CONSTRAINT_NAME,
               fk_cu.TABLE_SCHEMA, fk_cu.TABLE_NAME, fk_cu.COLUMN_NAME,
               pk_cu.TABLE_SCHEMA, pk_cu.TABLE_NAME, pk_cu.COLUMN_NAME
        FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS fk
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE fk_cu
            ON fk.CONSTRAINT_NAME = fk_cu.CONSTRAINT_NAME
        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE pk_cu
            ON fk.UNIQUE_CONSTRAINT_NAME = pk_cu.CONSTRAINT_NAME
           AND fk_cu.ORDINAL_POSITION = pk_cu.ORDINAL_POSITION
        """;
}
