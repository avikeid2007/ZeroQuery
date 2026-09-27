using Zeroquery.Core.Introspection;

namespace Zeroquery.Core.Persistence;

/// <summary>
/// A saved database connection profile (doc/Plan.md Section 6 &amp; Phase 7).
/// The connection string is encrypted at rest via Data Protection API.
/// </summary>
public sealed class SavedConnection
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DatabaseProvider Provider { get; init; }
    public required string EncryptedConnectionString { get; set; }
    public required string ConfigJson { get; set; }
    public required string ConnectionStringEnvVarName { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastConnectedAt { get; set; }
}
