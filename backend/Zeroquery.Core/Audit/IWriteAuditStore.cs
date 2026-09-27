namespace Zeroquery.Core.Audit;

/// <summary>
/// Abstraction for storing and querying database write audit log entries.
/// </summary>
public interface IWriteAuditStore
{
    /// <summary>Records an executed write operation into the audit store.</summary>
    Task RecordAsync(WriteAuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Retrieves recent write audit entries, optionally filtered by entity name.</summary>
    Task<IReadOnlyList<WriteAuditEntry>> GetRecentAsync(
        int limit = 50,
        string? entity = null,
        CancellationToken cancellationToken = default);
}
