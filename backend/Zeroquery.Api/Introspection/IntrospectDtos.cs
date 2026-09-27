using Zeroquery.Core.Introspection;

namespace Zeroquery.Api.Introspection;

/// <summary>Request body for POST /api/introspect.</summary>
/// <param name="Provider">Which database engine the connection string targets.</param>
/// <param name="ConnectionString">
/// Raw connection string, used only for the duration of this request. Not persisted,
/// not logged, and never echoed back in the response.
/// </param>
public sealed record IntrospectRequest(DatabaseProvider Provider, string ConnectionString);

public sealed record ColumnDto(string Name, string DataType, bool IsNullable, bool IsPrimaryKey, int? MaxLength)
{
    public static ColumnDto From(ColumnInfo c) => new(c.Name, c.DataType, c.IsNullable, c.IsPrimaryKey, c.MaxLength);
}

public sealed record TableDto(string Schema, string Name, bool IsView, IReadOnlyList<ColumnDto> Columns)
{
    public static TableDto From(TableInfo t) => new(t.Schema, t.Name, t.IsView, t.Columns.Select(ColumnDto.From).ToList());
}

public sealed record ForeignKeyDto(string ConstraintName, string FromTable, string FromColumn, string ToTable, string ToColumn)
{
    public static ForeignKeyDto From(ForeignKeyInfo f) => new(f.ConstraintName, f.FromTable, f.FromColumn, f.ToTable, f.ToColumn);
}

/// <summary>Response body for POST /api/introspect.</summary>
public sealed record IntrospectResponse(
    DatabaseProvider Provider,
    IReadOnlyList<TableDto> Tables,
    IReadOnlyList<ForeignKeyDto> ForeignKeys)
{
    public static IntrospectResponse From(SchemaInfo schema) => new(
        schema.Provider,
        schema.Tables.Select(TableDto.From).ToList(),
        schema.ForeignKeys.Select(ForeignKeyDto.From).ToList());
}

/// <summary>Uniform error payload returned on introspection failure.</summary>
public sealed record ErrorResponse(string Message);
