using Zeroquery.Core.Introspection;

namespace Zeroquery.Core.Security.Ssrf;

/// <summary>
/// Validates database connection strings against SSRF policies (blocking private/internal IP ranges).
/// </summary>
public interface ISsrfValidator
{
    /// <summary>
    /// Inspects the host/server in <paramref name="connectionString"/> and throws <see cref="SsrfException"/>
    /// if it resolves to a loopback, private network, or link-local/cloud-metadata IP range.
    /// </summary>
    Task ValidateConnectionStringAsync(DatabaseProvider provider, string connectionString, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inspects the host/server in <paramref name="connectionString"/> without provider specificity and
    /// throws <see cref="SsrfException"/> if it resolves to a loopback, private network, or link-local IP range.
    /// </summary>
    Task ValidateConnectionStringAsync(string connectionString, CancellationToken cancellationToken = default);
}
