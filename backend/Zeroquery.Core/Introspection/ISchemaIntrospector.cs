namespace Zeroquery.Core.Introspection;

/// <summary>
/// Reads structural metadata (tables, columns, foreign keys) from a database given a
/// connection string. Implementations must never read row data — introspection is a
/// metadata-only operation, run against a short-lived connection that is not persisted
/// unless the caller explicitly opts to save it after reviewing the schema.
/// </summary>
public interface ISchemaIntrospector
{
    /// <summary>The database engine this introspector targets.</summary>
    DatabaseProvider Provider { get; }

    /// <summary>
    /// Opens a short-lived connection using <paramref name="connectionString"/> and reads
    /// table/column/foreign-key metadata. Throws <see cref="SchemaIntrospectionException"/>
    /// on connection or query failures, with any provider-specific error detail sanitized
    /// out of the message (the connection string itself must never appear in exceptions/logs).
    /// </summary>
    Task<SchemaInfo> GetSchemaAsync(string connectionString, CancellationToken cancellationToken = default);
}
