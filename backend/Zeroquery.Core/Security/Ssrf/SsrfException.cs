namespace Zeroquery.Core.Security.Ssrf;

/// <summary>
/// Thrown when a database connection string targets an internal, loopback, or private network
/// address (SSRF mitigation, doc/Plan.md Section 3 &amp; Phase 6).
/// </summary>
public sealed class SsrfException : Exception
{
    public SsrfException(string message) : base(message)
    {
    }

    public SsrfException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
