namespace Zeroquery.Core.Security.DataProtection;

/// <summary>
/// Encrypts and decrypts connection strings at rest using ASP.NET Core Data Protection (doc/Plan.md Section 3 &amp; Phase 6).
/// </summary>
public interface IConnectionStringProtector
{
    /// <summary>
    /// Encrypts the raw connection string, returning a protected string representation.
    /// </summary>
    string Protect(string plainText);

    /// <summary>
    /// Decrypts a protected connection string. If the string is not protected (e.g. legacy plain text),
    /// returns it as-is.
    /// </summary>
    string Unprotect(string protectedOrPlainText);

    /// <summary>
    /// Checks whether the string was produced by <see cref="Protect"/>.
    /// </summary>
    bool IsProtected(string value);
}
