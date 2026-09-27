namespace Zeroquery.Core.Mutations;

public sealed record ExecuteMutationCommand(
    string Entity,
    string Operation,
    Dictionary<string, object?>? PrimaryKey,
    Dictionary<string, object?>? PreviousValues,
    Dictionary<string, object?> Values,
    string? ClientIp = null);

public sealed record MutationResult(
    bool Success,
    string Message,
    Dictionary<string, object?>? Record = null,
    bool IsForbidden = false);
