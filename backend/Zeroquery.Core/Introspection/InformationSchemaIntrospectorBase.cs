using System.Data.Common;

namespace Zeroquery.Core.Introspection;

/// <summary>
/// Shared ADO.NET-based implementation for introspectors that can express their metadata
/// reads as INFORMATION_SCHEMA-flavored SQL (SQL Server, PostgreSQL, MySQL all support this,
/// with small per-engine dialect differences supplied by subclasses via <see cref="TablesSql"/>,
/// <see cref="ColumnsSql"/>, <see cref="PrimaryKeysSql"/>, and <see cref="ForeignKeysSql"/>).
///
/// Runs four short, read-only metadata queries against a single short-lived connection:
/// tables/views, columns, primary-key columns, and foreign keys. No row data is ever read.
/// </summary>
public abstract class InformationSchemaIntrospectorBase : ISchemaIntrospector
{
    public abstract DatabaseProvider Provider { get; }

    /// <summary>Creates a new, unopened connection for the given connection string.</summary>
    protected abstract DbConnection CreateConnection(string connectionString);

    /// <summary>SQL returning columns: schema, table_name, is_view (bit/bool/int - truthy = view).</summary>
    protected abstract string TablesSql { get; }

    /// <summary>SQL returning columns: schema, table_name, column_name, data_type, is_nullable (truthy), max_length (nullable int).</summary>
    protected abstract string ColumnsSql { get; }

    /// <summary>SQL returning columns: schema, table_name, column_name for every PK column.</summary>
    protected abstract string PrimaryKeysSql { get; }

    /// <summary>SQL returning columns: constraint_name, from_schema, from_table, from_column, to_schema, to_table, to_column.</summary>
    protected abstract string ForeignKeysSql { get; }

    /// <summary>Wraps provider-specific connection/query failures into a safe, connection-string-free message.</summary>
    protected virtual string DescribeFailure(Exception ex) =>
        $"Could not read schema from the database. Verify the connection string, network access, and that the account has metadata read permissions. ({ex.GetType().Name})";

    public async Task<SchemaInfo> GetSchemaAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new SchemaIntrospectionException("Connection string must not be empty.");
        }

        await using var connection = CreateConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new SchemaIntrospectionException(DescribeFailure(ex), ex);
        }

        try
        {
            var tables = await ReadTablesAsync(connection, cancellationToken).ConfigureAwait(false);
            var columnsByTable = await ReadColumnsAsync(connection, cancellationToken).ConfigureAwait(false);
            var primaryKeys = await ReadPrimaryKeysAsync(connection, cancellationToken).ConfigureAwait(false);
            var foreignKeys = await ReadForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

            var tableInfos = new List<TableInfo>(tables.Count);
            foreach (var (schema, name, isView) in tables)
            {
                var key = (schema, name);
                var columns = columnsByTable.TryGetValue(key, out var cols) ? cols : new List<ColumnInfo>();
                var pkColumnNames = primaryKeys.TryGetValue(key, out var pks) ? pks : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var withPkFlag = columns
                    .Select(c => c with { IsPrimaryKey = pkColumnNames.Contains(c.Name) })
                    .ToList();

                tableInfos.Add(new TableInfo(schema, name, isView, withPkFlag));
            }

            return new SchemaInfo(Provider, tableInfos, foreignKeys);
        }
        catch (SchemaIntrospectionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SchemaIntrospectionException(DescribeFailure(ex), ex);
        }
    }

    private async Task<List<(string Schema, string Name, bool IsView)>> ReadTablesAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        var result = new List<(string, string, bool)>();

        await using var command = connection.CreateCommand();
        command.CommandText = TablesSql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);
            var isView = ToBool(reader.GetValue(2));
            result.Add((schema, name, isView));
        }

        return result;
    }

    private async Task<Dictionary<(string Schema, string Table), List<ColumnInfo>>> ReadColumnsAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string, string), List<ColumnInfo>>();

        await using var command = connection.CreateCommand();
        command.CommandText = ColumnsSql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var column = reader.GetString(2);
            var dataType = reader.GetString(3);
            var isNullable = ToBool(reader.GetValue(4));
            int? maxLength = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5));

            var key = (schema, table);
            if (!result.TryGetValue(key, out var list))
            {
                list = new List<ColumnInfo>();
                result[key] = list;
            }

            list.Add(new ColumnInfo(column, dataType, isNullable, IsPrimaryKey: false, maxLength));
        }

        return result;
    }

    private async Task<Dictionary<(string Schema, string Table), HashSet<string>>> ReadPrimaryKeysAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string, string), HashSet<string>>();

        await using var command = connection.CreateCommand();
        command.CommandText = PrimaryKeysSql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var column = reader.GetString(2);

            var key = (schema, table);
            if (!result.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[key] = set;
            }

            set.Add(column);
        }

        return result;
    }

    private async Task<List<ForeignKeyInfo>> ReadForeignKeysAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        var result = new List<ForeignKeyInfo>();

        await using var command = connection.CreateCommand();
        command.CommandText = ForeignKeysSql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var constraintName = reader.GetString(0);
            var fromSchema = reader.GetString(1);
            var fromTable = reader.GetString(2);
            var fromColumn = reader.GetString(3);
            var toSchema = reader.GetString(4);
            var toTable = reader.GetString(5);
            var toColumn = reader.GetString(6);

            var fromQualified = string.IsNullOrEmpty(fromSchema) ? fromTable : $"{fromSchema}.{fromTable}";
            var toQualified = string.IsNullOrEmpty(toSchema) ? toTable : $"{toSchema}.{toTable}";

            result.Add(new ForeignKeyInfo(constraintName, fromQualified, fromColumn, toQualified, toColumn));
        }

        return result;
    }

    private static bool ToBool(object value) => value switch
    {
        bool b => b,
        byte b => b != 0,
        short s => s != 0,
        int i => i != 0,
        long l => l != 0,
        string s => s.Equals("YES", StringComparison.OrdinalIgnoreCase) || s == "1",
        _ => false
    };
}
