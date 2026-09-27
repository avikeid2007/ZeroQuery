namespace Zeroquery.Core.Mutations;

/// <summary>
/// Service responsible for validating permissions, executing confirmed writes against
/// DAB instances, and recording entries in the write audit log.
/// </summary>
public interface IMutationService
{
    /// <summary>
    /// Executes a confirmed database mutation against the specified running instance.
    /// </summary>
    Task<MutationResult> ExecuteAsync(
        string instanceId,
        ExecuteMutationCommand command,
        CancellationToken cancellationToken = default);
}
