namespace Zeroquery.Core.Introspection;

/// <summary>
/// A single column within an introspected table or view.
/// </summary>
/// <param name="Name">Column name as reported by the database.</param>
/// <param name="DataType">Database-native data type name (e.g. "nvarchar", "integer", "int").</param>
/// <param name="IsNullable">Whether the column allows NULL values.</param>
/// <param name="IsPrimaryKey">Whether the column participates in the table's primary key.</param>
/// <param name="MaxLength">Character/byte max length, if applicable to the type (null otherwise).</param>
public sealed record ColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    int? MaxLength = null);

/// <summary>
/// A detected foreign-key relationship from one table/column to another.
/// </summary>
/// <param name="ConstraintName">Name of the FK constraint.</param>
/// <param name="FromTable">Schema-qualified table that owns the FK column.</param>
/// <param name="FromColumn">Column on <see cref="FromTable"/> holding the foreign key value.</param>
/// <param name="ToTable">Schema-qualified table being referenced.</param>
/// <param name="ToColumn">Column on <see cref="ToTable"/> being referenced (usually its PK).</param>
public sealed record ForeignKeyInfo(
    string ConstraintName,
    string FromTable,
    string FromColumn,
    string ToTable,
    string ToColumn);

/// <summary>
/// A single table or view discovered during introspection, with its columns.
/// Relationships are reported separately at the schema level in <see cref="SchemaInfo"/>
/// since a single FK touches two tables.
/// </summary>
/// <param name="Schema">Database schema name (e.g. "dbo", "public"). May be empty for engines without schemas (MySQL).</param>
/// <param name="Name">Table or view name.</param>
/// <param name="IsView">True if this is a view rather than a base table.</param>
/// <param name="Columns">Ordered list of columns.</param>
public sealed record TableInfo(
    string Schema,
    string Name,
    bool IsView,
    IReadOnlyList<ColumnInfo> Columns)
{
    /// <summary>Schema-qualified identifier, e.g. "dbo.Orders" or just "Orders" when there's no schema.</summary>
    public string QualifiedName => string.IsNullOrEmpty(Schema) ? Name : $"{Schema}.{Name}";
}

/// <summary>
/// The full result of introspecting a database: every exposable table/view plus
/// detected foreign-key relationships between them.
/// </summary>
/// <param name="Provider">Which database engine this schema was read from.</param>
/// <param name="Tables">All discovered tables/views.</param>
/// <param name="ForeignKeys">All discovered foreign-key relationships across the schema.</param>
public sealed record SchemaInfo(
    DatabaseProvider Provider,
    IReadOnlyList<TableInfo> Tables,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys);
