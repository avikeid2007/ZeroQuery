using Zeroquery.Core.Audit;
using Zeroquery.Core.Mutations;

namespace Zeroquery.Api.Mutations;

public sealed record ExecuteMutationRequest(
    string Entity,
    string Operation,
    Dictionary<string, object?>? PrimaryKey,
    Dictionary<string, object?>? PreviousValues,
    Dictionary<string, object?> Values)
{
    public ExecuteMutationCommand ToCommand(string? clientIp) =>
        new(Entity, Operation, PrimaryKey, PreviousValues, Values, clientIp);
}

public sealed record MutationResultDto(
    bool Success,
    string Message,
    Dictionary<string, object?>? Record = null)
{
    public static MutationResultDto From(MutationResult r) =>
        new(r.Success, r.Message, r.Record);
}

public sealed record WriteAuditEntryDto(
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
    string? ErrorMessage)
{
    public static WriteAuditEntryDto From(WriteAuditEntry e) => new(
        e.Id,
        e.Timestamp,
        e.ClientIp,
        e.InstanceId,
        e.Entity,
        e.Operation,
        e.PrimaryKey,
        e.PreviousValues,
        e.NewValues,
        e.Success,
        e.ErrorMessage);
}
