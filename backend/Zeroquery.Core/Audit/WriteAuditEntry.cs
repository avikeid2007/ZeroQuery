namespace Zeroquery.Core.Audit;

/// <summary>
/// Immutable record of a database write operation executed through Zeroquery (Phase 8 CRUD).
/// Maintained in a dedicated audit log separate from general application telemetry.
/// </summary>
public sealed record WriteAuditEntry(
    string Id,
    DateTimeOffset Timestamp,
    string ClientIp,
    string InstanceId,
    string Entity,
    string Operation,
    Dictionary<string, object?>? PrimaryKey,
    Dictionary<string, object?>? PreviousValues,
    Dictionary<string, object?>? NewValues,
    bool Success,
    string? ErrorMessage = null);
