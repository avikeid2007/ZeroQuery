namespace Zeroquery.Core.ProcessManagement;

/// <summary>
/// Thrown for process-manager-level failures (no free port available, startup cap reached,
/// unknown instance id, etc). Messages are safe to show to a user.
/// </summary>
public sealed class DabProcessManagerException : Exception
{
    public DabProcessManagerException(string message) : base(message)
    {
    }

    public DabProcessManagerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
