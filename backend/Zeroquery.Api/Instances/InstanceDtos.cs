using Zeroquery.Core.ProcessManagement;

namespace Zeroquery.Api.Instances;

/// <summary>
/// Request body for POST /api/instances. Starts a DAB subprocess from a config previously
/// produced by POST /api/config/generate.
/// </summary>
/// <param name="ConfigJson">The dab-config.json text to write to disk and launch DAB against.</param>
/// <param name="ConnectionStringEnvVarName">
/// Env var name the config's <c>@env('NAME')</c> reference expects (must match what was
/// passed to /api/config/generate).
/// </param>
/// <param name="ConnectionString">
/// The real connection string, set as that env var on the child process only — never written
/// to the config file on disk, never logged (see doc/Plan.md Section 3).
/// </param>
public sealed record StartInstanceRequest(
    string ConfigJson,
    string ConnectionStringEnvVarName,
    string ConnectionString);

/// <summary>Response for POST /api/instances and GET /api/instances/{id}/status.</summary>
public sealed record InstanceStatusResponse(
    string Id,
    string Status,
    int Port,
    string BaseUrl,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    string? LastError)
{
    public static InstanceStatusResponse From(DabInstance instance, DabInstanceStatus status) => new(
        instance.Id,
        status.ToString(),
        instance.Port,
        instance.BaseUrl,
        instance.StartedAt,
        instance.LastUsedAt,
        instance.LastError);
}
