using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Persistence;

namespace Zeroquery.Tests.Persistence;

public class FileSavedConnectionStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileSavedConnectionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "zeroquery-tests-" + Guid.NewGuid().ToString("n"));
    }

    private FileSavedConnectionStore CreateStore(string mode = "save")
    {
        var options = Options.Create(new PersistenceOptions
        {
            Mode = mode,
            StorageDirectory = _tempDir
        });

        return new FileSavedConnectionStore(options, NullLogger<FileSavedConnectionStore>.Instance);
    }

    [Fact]
    public async Task SaveAsync_And_GetAllAsync_ReturnsSavedConnection()
    {
        using var store = CreateStore();
        var conn = new SavedConnection
        {
            Id = "conn-1",
            Name = "Production DB",
            Provider = DatabaseProvider.SqlServer,
            EncryptedConnectionString = "zqenc:v1:encrypted",
            ConfigJson = "{}",
            ConnectionStringEnvVarName = "ZQ_DB_CONN",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await store.SaveAsync(conn);
        var all = await store.GetAllAsync();

        Assert.Single(all);
        Assert.Equal("conn-1", all[0].Id);
        Assert.Equal("Production DB", all[0].Name);
        Assert.Equal(DatabaseProvider.SqlServer, all[0].Provider);
    }

    [Fact]
    public async Task SaveAsync_PersistsToDisk_AndSurvivesNewStoreInstance()
    {
        var conn = new SavedConnection
        {
            Id = "conn-persist",
            Name = "Persistent DB",
            Provider = DatabaseProvider.PostgreSql,
            EncryptedConnectionString = "zqenc:v1:secret",
            ConfigJson = "{}",
            ConnectionStringEnvVarName = "ZQ_DB_CONN",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // First instance saves
        using (var store1 = CreateStore(mode: "save"))
        {
            await store1.SaveAsync(conn);
        }

        // Second instance reads from the same directory
        using (var store2 = CreateStore(mode: "save"))
        {
            var loaded = await store2.GetByIdAsync("conn-persist");
            Assert.NotNull(loaded);
            Assert.Equal("Persistent DB", loaded.Name);
            Assert.Equal(DatabaseProvider.PostgreSql, loaded.Provider);
            Assert.Equal("zqenc:v1:secret", loaded.EncryptedConnectionString);
        }
    }

    [Fact]
    public async Task SessionOnlyMode_DoesNotPersistToDisk()
    {
        var conn = new SavedConnection
        {
            Id = "conn-session",
            Name = "Session DB",
            Provider = DatabaseProvider.MySql,
            EncryptedConnectionString = "zqenc:v1:temp",
            ConfigJson = "{}",
            ConnectionStringEnvVarName = "ZQ_DB_CONN",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // First instance saves in session-only mode
        using (var store1 = CreateStore(mode: "session-only"))
        {
            await store1.SaveAsync(conn);
            var inMemory = await store1.GetByIdAsync("conn-session");
            Assert.NotNull(inMemory);
        }

        // File should not exist on disk
        Assert.False(File.Exists(Path.Combine(_tempDir, "connections.json")));

        // Second instance should NOT find it
        using (var store2 = CreateStore(mode: "session-only"))
        {
            var notFound = await store2.GetByIdAsync("conn-session");
            Assert.Null(notFound);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesConnection_AndUpdatesDisk()
    {
        using (var store1 = CreateStore(mode: "save"))
        {
            await store1.SaveAsync(new SavedConnection
            {
                Id = "conn-to-delete",
                Name = "Delete Me",
                Provider = DatabaseProvider.SqlServer,
                EncryptedConnectionString = "zqenc:v1:temp",
                ConfigJson = "{}",
                ConnectionStringEnvVarName = "ZQ_DB_CONN",
                CreatedAt = DateTimeOffset.UtcNow
            });

            var deleted = await store1.DeleteAsync("conn-to-delete");
            Assert.True(deleted);

            var notFound = await store1.GetByIdAsync("conn-to-delete");
            Assert.Null(notFound);
        }

        using (var store2 = CreateStore(mode: "save"))
        {
            var all = await store2.GetAllAsync();
            Assert.Empty(all);
        }
    }

    [Fact]
    public async Task UpdateLastConnectedAsync_UpdatesTimestamp()
    {
        using var store = CreateStore(mode: "save");
        await store.SaveAsync(new SavedConnection
        {
            Id = "conn-timestamp",
            Name = "Timestamp DB",
            Provider = DatabaseProvider.SqlServer,
            EncryptedConnectionString = "zqenc:v1:temp",
            ConfigJson = "{}",
            ConnectionStringEnvVarName = "ZQ_DB_CONN",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var now = DateTimeOffset.UtcNow;
        var updated = await store.UpdateLastConnectedAsync("conn-timestamp", now);

        Assert.NotNull(updated);
        Assert.Equal(now, updated.LastConnectedAt);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
