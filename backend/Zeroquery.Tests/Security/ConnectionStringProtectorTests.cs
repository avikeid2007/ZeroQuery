using Microsoft.AspNetCore.DataProtection;
using Zeroquery.Core.Security.DataProtection;

namespace Zeroquery.Tests.Security;

public class ConnectionStringProtectorTests
{
    private sealed class FakeDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext)
        {
            var result = new byte[plaintext.Length];
            for (var i = 0; i < plaintext.Length; i++) result[i] = (byte)(plaintext[i] ^ 0x42);
            return result;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            var result = new byte[protectedData.Length];
            for (var i = 0; i < protectedData.Length; i++) result[i] = (byte)(protectedData[i] ^ 0x42);
            return result;
        }
    }

    private sealed class FakeDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new FakeDataProtector();
    }

    private static DataProtectionConnectionStringProtector CreateProtector()
    {
        return new DataProtectionConnectionStringProtector(new FakeDataProtectionProvider());
    }

    [Fact]
    public void Protect_EncryptsString_AndAddsPrefix()
    {
        var protector = CreateProtector();
        const string raw = "Server=myserver;Database=db;User Id=usr;Password=pwd;";

        var protectedStr = protector.Protect(raw);

        Assert.StartsWith(DataProtectionConnectionStringProtector.Prefix, protectedStr);
        Assert.NotEqual(raw, protectedStr);
    }

    [Fact]
    public void Unprotect_DecryptsBackToOriginal()
    {
        var protector = CreateProtector();
        const string raw = "Server=myserver;Database=db;User Id=usr;Password=pwd;";

        var protectedStr = protector.Protect(raw);
        var recovered = protector.Unprotect(protectedStr);

        Assert.Equal(raw, recovered);
    }

    [Fact]
    public void Unprotect_ReturnsPlaintextAsIs_WhenNotProtected()
    {
        var protector = CreateProtector();
        const string raw = "Server=myserver;Database=db;User Id=usr;Password=pwd;";

        var result = protector.Unprotect(raw);

        Assert.Equal(raw, result);
    }

    [Fact]
    public void Protect_IsIdempotent_WhenAlreadyProtected()
    {
        var protector = CreateProtector();
        const string raw = "Server=myserver;Database=db;";

        var protectedOnce = protector.Protect(raw);
        var protectedTwice = protector.Protect(protectedOnce);

        Assert.Equal(protectedOnce, protectedTwice);
    }

    [Fact]
    public void IsProtected_IdentifiesProtectedStrings()
    {
        var protector = CreateProtector();
        const string raw = "Server=myserver;";

        Assert.False(protector.IsProtected(raw));
        Assert.False(protector.IsProtected(""));
        Assert.False(protector.IsProtected(null!));

        var protectedStr = protector.Protect(raw);
        Assert.True(protector.IsProtected(protectedStr));
    }
}
