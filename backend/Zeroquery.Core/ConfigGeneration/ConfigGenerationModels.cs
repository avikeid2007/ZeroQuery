using Zeroquery.Core.Introspection;

namespace Zeroquery.Core.ConfigGeneration;

/// <summary>
/// One column's inclusion/metadata choice from the Phase 1 picker, feeding into the
/// generated entity's <c>fields</c> array and read-permission <c>fields.exclude</c> list.
/// </summary>
/// <param name="Name">Database column name (must match a column from introspection).</param>
/// <param name="Include">Whether this column is exposed via the generated entity at all.</param>
/// <param name="IsPrimaryKey">Whether the column is a primary key (informs DAB's <c>fields[].primary-key</c>, mainly needed for views).</param>
/// <param name="Description">Optional user-supplied description; improves MCP tool-selection accuracy.</param>
public sealed record ColumnSelectionRequest(
    string Name,
    bool Include,
    bool IsPrimaryKey,
    string? Description = null);

/// <summary>
/// One table/view's picker choices: which entity name to expose it as, its description,
/// which columns to include, and which write operations (if any) are opted into.
/// </summary>
/// <param name="Schema">Database schema (empty for MySQL).</param>
/// <param name="TableName">Underlying table/view name.</param>
/// <param name="IsView">Whether the source object is a view rather than a base table.</param>
/// <param name="Columns">Per-column picker choices. Must contain at least one included column.</param>
/// <param name="EntityName">
/// Optional override for the DAB entity key. Defaults to <see cref="TableName"/>; the generator
/// resolves collisions (e.g. same table name across schemas) by prefixing with the schema.
/// </param>
/// <param name="Description">Optional human-readable entity description, surfaced to MCP clients.</param>
/// <param name="WriteActions">
/// Opt-in write actions (subset of "create", "update", "delete"). Empty by default — Zeroquery
/// defaults every entity to read-only per doc/Plan.md Section 3; full CRUD is Phase 8 scope.
/// </param>
public sealed record EntitySelectionRequest(
    string Schema,
    string TableName,
    bool IsView,
    IReadOnlyList<ColumnSelectionRequest> Columns,
    string? EntityName = null,
    string? Description = null,
    IReadOnlyList<string>? WriteActions = null)
{
    public string QualifiedTableName => string.IsNullOrEmpty(Schema) ? TableName : $"{Schema}.{TableName}";
}

/// <summary>
/// Full request to generate a <c>dab-config.json</c> from Phase 1 picker output.
/// </summary>
/// <param name="Provider">Database engine — mapped to DAB's <c>data-source.database-type</c>.</param>
/// <param name="ConnectionStringEnvVarName">
/// Name of the environment variable DAB should read its connection string from at startup
/// (referenced in the generated config as <c>@env('NAME')</c>). Zeroquery never writes the raw
/// connection string into the generated config file — see doc/Plan.md Section 3.
/// </param>
/// <param name="Entities">Selected tables/views and their column/permission choices.</param>
/// <param name="EnableRest">Whether to enable DAB's REST endpoint (/api).</param>
/// <param name="EnableGraphQL">Whether to enable DAB's GraphQL endpoint (/graphql).</param>
public sealed record ConfigGenerationRequest(
    DatabaseProvider Provider,
    string ConnectionStringEnvVarName,
    IReadOnlyList<EntitySelectionRequest> Entities,
    bool EnableRest = true,
    bool EnableGraphQL = true);

/// <summary>Result of generating (and validating) a dab-config.json.</summary>
/// <param name="ConfigJson">Pretty-printed JSON text of the generated configuration.</param>
/// <param name="IsValid">Whether the generated JSON validates against DAB's published JSON schema.</param>
/// <param name="ValidationErrors">Human-readable validation error messages, empty when <see cref="IsValid"/> is true.</param>
public sealed record ConfigGenerationResult(
    string ConfigJson,
    bool IsValid,
    IReadOnlyList<string> ValidationErrors);
