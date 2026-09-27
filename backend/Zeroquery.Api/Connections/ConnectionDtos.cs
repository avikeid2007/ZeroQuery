using Zeroquery.Core.Introspection;
using Zeroquery.Core.Persistence;

namespace Zeroquery.Api.Connections;

/// <summary>Request body for saving a connection profile (POST /api/connections).</summary>
public sealed record SaveConnectionRequest(
    string? Name,
    DatabaseProvider Provider,
    string ConnectionString,
    string ConfigJson,
    string ConnectionStringEnvVarName);

/// <summary>Summary representation for listing saved connections (GET /api/connections).</summary>
public sealed record SavedConnectionSummaryResponse(
    string Id,
    string Name,
    string Provider,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastConnectedAt)
{
    public static SavedConnectionSummaryResponse From(SavedConnection c) => new(
        c.Id,
        c.Name,
        c.Provider.ToString(),
        c.CreatedAt,
        c.LastConnectedAt);
}

/// <summary>Detail representation for confirming reconnection (GET /api/connections/{id}).</summary>
public sealed record SavedConnectionDetailResponse(
    string Id,
    string Name,
    string Provider,
    string ConnectionStringEnvVarName,
    string ConfigJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastConnectedAt)
{
    public static SavedConnectionDetailResponse From(SavedConnection c) => new(
        c.Id,
        c.Name,
        c.Provider.ToString(),
        c.ConnectionStringEnvVarName,
        c.ConfigJson,
        c.CreatedAt,
        c.LastConnectedAt);
}
