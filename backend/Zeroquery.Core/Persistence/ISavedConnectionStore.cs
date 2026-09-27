namespace Zeroquery.Core.Persistence;

/// <summary>
/// Storage contract for managing saved database connections (doc/Plan.md Section 6 &amp; Phase 7).
/// </summary>
public interface ISavedConnectionStore
{
    /// <summary>Returns all saved connections ordered by LastConnectedAt (descending) or CreatedAt.</summary>
    Task<IReadOnlyList<SavedConnection>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a saved connection by id, or null if not found.</summary>
    Task<SavedConnection?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Saves or updates a connection.</summary>
    Task<SavedConnection> SaveAsync(SavedConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Deletes a saved connection by id ("forget this connection"). Returns true if removed, false if not found.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Updates the LastConnectedAt timestamp for a connection.</summary>
    Task<SavedConnection?> UpdateLastConnectedAsync(string id, DateTimeOffset connectedAt, CancellationToken cancellationToken = default);
}
