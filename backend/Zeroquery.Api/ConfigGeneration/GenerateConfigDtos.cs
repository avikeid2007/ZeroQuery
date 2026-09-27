using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;

namespace Zeroquery.Api.ConfigGeneration;

/// <summary>Column picker choice, as submitted by the frontend for POST /api/config/generate.</summary>
public sealed record ColumnSelectionDto(string Name, bool Include, bool IsPrimaryKey, string? Description)
{
    public ColumnSelectionRequest ToCoreRequest() => new(Name, Include, IsPrimaryKey, Description);
}

/// <summary>Table/view picker choice, as submitted by the frontend for POST /api/config/generate.</summary>
public sealed record EntitySelectionDto(
    string Schema,
    string TableName,
    bool IsView,
    IReadOnlyList<ColumnSelectionDto> Columns,
    string? EntityName,
    string? Description,
    IReadOnlyList<string>? WriteActions)
{
    public EntitySelectionRequest ToCoreRequest() => new(
        Schema,
        TableName,
        IsView,
        Columns.Select(c => c.ToCoreRequest()).ToList(),
        EntityName,
        Description,
        WriteActions);
}

/// <summary>Request body for POST /api/config/generate.</summary>
/// <param name="Provider">Database engine, must match the provider used for introspection.</param>
/// <param name="ConnectionStringEnvVarName">
/// Name of the environment variable the generated config will reference via <c>@env(...)</c>.
/// Zeroquery never writes the raw connection string into the generated file.
/// </param>
/// <param name="Entities">Selected tables/views with their column and permission choices.</param>
/// <param name="EnableRest">Whether to enable DAB REST endpoints.</param>
/// <param name="EnableGraphQL">Whether to enable DAB GraphQL endpoint.</param>
public sealed record GenerateConfigRequest(
    DatabaseProvider Provider,
    string ConnectionStringEnvVarName,
    IReadOnlyList<EntitySelectionDto> Entities,
    bool? EnableRest = true,
    bool? EnableGraphQL = true);

/// <summary>Response body for POST /api/config/generate.</summary>
public sealed record GenerateConfigResponse(string ConfigJson, bool IsValid, IReadOnlyList<string> ValidationErrors)
{
    public static GenerateConfigResponse From(ConfigGenerationResult result) =>
        new(result.ConfigJson, result.IsValid, result.ValidationErrors);
}
