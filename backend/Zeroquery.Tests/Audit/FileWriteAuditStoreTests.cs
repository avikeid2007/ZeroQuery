using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Audit;
using Zeroquery.Core.Persistence;

namespace Zeroquery.Tests.Audit;

public class FileWriteAuditStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileWriteAuditStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zq-audit-test-{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RecordAsync_StoresEntry_AndCanRetrieveRecent()
    {
        var options = Options.Create(new PersistenceOptions
        {
            Mode = "save",
            StorageDirectory = _tempDir
        });

        var store = new FileWriteAuditStore(options, NullLogger<FileWriteAuditStore>.Instance);

        var entry = new WriteAuditEntry(
            Id: Guid.NewGuid().ToString("n"),
            Timestamp: DateTimeOffset.UtcNow,
            ClientIp: "127.0.0.1",
            InstanceId: "inst-1",
            Entity: "Products",
            Operation: "update",
            PrimaryKey: new Dictionary<string, object?> { ["ProductID"] = 1 },
            PreviousValues: new Dictionary<string, object?> { ["UnitPrice"] = 18.0 },
            NewValues: new Dictionary<string, object?> { ["UnitPrice"] = 19.99 },
            Success: true);

        await store.RecordAsync(entry);

        var recent = await store.GetRecentAsync(10);

        Assert.Single(recent);
        Assert.Equal("Products", recent[0].Entity);
        Assert.Equal("update", recent[0].Operation);
        Assert.True(recent[0].Success);
    }

    [Fact]
    public async Task GetRecentAsync_FiltersByEntity()
    {
        var options = Options.Create(new PersistenceOptions
        {
            Mode = "session-only"
        });

        var store = new FileWriteAuditStore(options, NullLogger<FileWriteAuditStore>.Instance);

        await store.RecordAsync(new WriteAuditEntry(
            "1", DateTimeOffset.UtcNow.AddMinutes(-2), "127.0.0.1", "inst-1", "Products", "update", null, null, new(), true));
        await store.RecordAsync(new WriteAuditEntry(
            "2", DateTimeOffset.UtcNow.AddMinutes(-1), "127.0.0.1", "inst-1", "Orders", "create", null, null, new(), true));
        await store.RecordAsync(new WriteAuditEntry(
            "3", DateTimeOffset.UtcNow, "127.0.0.1", "inst-1", "Products", "delete", null, null, new(), true));

        var productAudits = await store.GetRecentAsync(entity: "Products");
        var orderAudits = await store.GetRecentAsync(entity: "Orders");

        Assert.Equal(2, productAudits.Count);
        Assert.Single(orderAudits);
        Assert.Equal("Orders", orderAudits[0].Entity);
    }

    [Fact]
    public async Task RecordAsync_PersistsToDisk_AndCanBeReloadedByNewInstance()
    {
        var options = Options.Create(new PersistenceOptions
        {
            Mode = "save",
            StorageDirectory = _tempDir
        });

        var store1 = new FileWriteAuditStore(options, NullLogger<FileWriteAuditStore>.Instance);
        await store1.RecordAsync(new WriteAuditEntry(
            "test-id", DateTimeOffset.UtcNow, "10.0.0.1", "inst-99", "Customers", "create", null, null, new(), true));

        // Create a new store instance pointing to same directory
        var store2 = new FileWriteAuditStore(options, NullLogger<FileWriteAuditStore>.Instance);
        var entries = await store2.GetRecentAsync(10);

        Assert.Single(entries);
        Assert.Equal("Customers", entries[0].Entity);
        Assert.Equal("inst-99", entries[0].InstanceId);
    }

    [Fact]
    public async Task RecordAsync_DoesNotPersistToDisk_WhenSessionOnly()
    {
        var options = Options.Create(new PersistenceOptions
        {
            Mode = "session-only",
            StorageDirectory = _tempDir
        });

        var store = new FileWriteAuditStore(options, NullLogger<FileWriteAuditStore>.Instance);
        await store.RecordAsync(new WriteAuditEntry(
            "test-id", DateTimeOffset.UtcNow, "10.0.0.1", "inst-99", "Customers", "create", null, null, new(), true));

        var auditFile = Path.Combine(_tempDir, "audit-log.json");
        Assert.False(File.Exists(auditFile));
    }
}
