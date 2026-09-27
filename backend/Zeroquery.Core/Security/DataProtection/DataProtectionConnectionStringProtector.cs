using Microsoft.AspNetCore.DataProtection;

namespace Zeroquery.Core.Security.DataProtection;

/// <summary>
/// Implements <see cref="IConnectionStringProtector"/> using ASP.NET Core Data Protection.
/// Encrypted payloads are prefixed with <c>zqenc:v1:</c> to distinguish them from plain text.
/// </summary>
public sealed class DataProtectionConnectionStringProtector : IConnectionStringProtector
{
    public const string Prefix = "zqenc:v1:";
    private const string Purpose = "Zeroquery.ConnectionStringProtector.v1";

    private readonly IDataProtector _protector;

    public DataProtectionConnectionStringProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return plainText;
        }

        if (IsProtected(plainText))
        {
            return plainText;
        }

        var cipherText = _protector.Protect(plainText);
        return $"{Prefix}{cipherText}";
    }

    public string Unprotect(string protectedOrPlainText)
    {
        if (string.IsNullOrWhiteSpace(protectedOrPlainText))
        {
            return protectedOrPlainText;
        }

        if (!IsProtected(protectedOrPlainText))
        {
            return protectedOrPlainText;
        }

        var cipherText = protectedOrPlainText[Prefix.Length..];
        return _protector.Unprotect(cipherText);
    }

    public bool IsProtected(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
