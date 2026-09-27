using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.Security.DataProtection;
using Zeroquery.Core.Security.Ssrf;

namespace Zeroquery.Tests.Connections;

public class ConnectionEndpointsTests : IDisposable
{
    private sealed class FakeDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext)
        {
            var result = new byte[plaintext.Length];
            for (var i = 0; i < plaintext.Length; i++) result[i] = (byte)(plaintext[i] ^ 0x5A);
            return result;
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            var result = new byte[protectedData.Length];
            for (var i = 0; i < protectedData.Length; i++) result[i] = (byte)(protectedData[i] ^ 0x5A);
            return result;
        }
    }

    private sealed class FakeDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new FakeDataProtector();
    }

    private readonly string _tempDir;
    private readonly FileSavedConnectionStore _store;
    private readonly SsrfValidator _ssrfValidator;
    private readonly DataProtectionConnectionStringProtector _protector;

    public ConnectionEndpointsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "zeroquery-conn-tests-" + Guid.NewGuid().ToString("n"));
        _store = new FileSavedConnectionStore(
            Options.Create(new PersistenceOptions { Mode = "save", StorageDirectory = _tempDir }),
            NullLogger<FileSavedConnectionStore>.Instance);
        _ssrfValidator = new SsrfValidator(
            Options.Create(new SsrfOptions { BlockPrivateNetworks = true }),
            NullLogger<SsrfValidator>.Instance);
        _protector = new DataProtectionConnectionStringProtector(new FakeDataProtectionProvider());
    }

    [Fact]
    public async Task Save_BlocksPrivateNetworkSSRF()
    {
        var rawConn = "Server=127.0.0.1;Database=test;";

        await Assert.ThrowsAsync<SsrfException>(() =>
            _ssrfValidator.ValidateConnectionStringAsync(DatabaseProvider.SqlServer, rawConn));
    }

    [Fact]
    public async Task Save_StoresEncryptedConnectionString_AndRecoversInStore()
    {
        const string rawConn = "Server=93.184.216.34;Database=mydb;User Id=usr;Password=pwd;";

        // Validate SSRF
        await _ssrfValidator.ValidateConnectionStringAsync(DatabaseProvider.SqlServer, rawConn);

        // Encrypt and save
        var encrypted = _protector.Protect(rawConn);
        Assert.StartsWith(DataProtectionConnectionStringProtector.Prefix, encrypted);

        var saved = new SavedConnection
        {
            Id = "test-123",
            Name = "My Database",
            Provider = DatabaseProvider.SqlServer,
            EncryptedConnectionString = encrypted,
            ConfigJson = "{}",
            ConnectionStringEnvVarName = "ZQ_DB_CONN"
        };

        await _store.SaveAsync(saved);

        var retrieved = await _store.GetByIdAsync("test-123");
        Assert.NotNull(retrieved);
        Assert.Equal("My Database", retrieved.Name);

        // Decrypt
        var decrypted = _protector.Unprotect(retrieved.EncryptedConnectionString);
        Assert.Equal(rawConn, decrypted);
    }

    [Fact]
    public async Task Forget_RemovesConnectionFromStore()
    {
        var saved = new SavedConnection
        {
            Id = "to-forget",
            Name = "Forget DB",
            Provider = DatabaseProvider.PostgreSql,
            EncryptedConnectionString = "zqenc:v1:abc",
            ConfigJson = "{}",
            ConnectionStringEnvVarName = "ZQ_DB_CONN"
        };

        await _store.SaveAsync(saved);
        var deleted = await _store.DeleteAsync("to-forget");
        Assert.True(deleted);

        var retrieved = await _store.GetByIdAsync("to-forget");
        Assert.Null(retrieved);
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
