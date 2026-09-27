using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Audit;
using Zeroquery.Core.ConfigGeneration;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Mcp;
using Zeroquery.Core.Mutations;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.ProcessManagement;
using Zeroquery.Tests.Orchestration;

namespace Zeroquery.Tests.Mutations;

public class MutationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DabProcessManager _processManager;
    private readonly FileWriteAuditStore _auditStore;

    public MutationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zq-mutation-test-{Guid.NewGuid():n}");
        Directory.CreateDirectory(_tempDir);

        _processManager = new DabProcessManager(
            Options.Create(new DabProcessManagerOptions()),
            NullLogger<DabProcessManager>.Instance);

        _auditStore = new FileWriteAuditStore(
            Options.Create(new PersistenceOptions { Mode = "session-only" }),
            NullLogger<FileWriteAuditStore>.Instance);
    }

    public void Dispose()
    {
        _processManager.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private string WriteConfigFile(IReadOnlyList<string>? writeActions)
    {
        var configJson = DabConfigGenerator.Generate(new ConfigGenerationRequest(
            Provider: DatabaseProvider.SqlServer,
            ConnectionStringEnvVarName: "TEST_CONN",
            Entities: new[]
            {
                new EntitySelectionRequest(
                    Schema: "dbo",
                    TableName: "Products",
                    IsView: false,
                    Columns: new[]
                    {
                        new ColumnSelectionRequest("ProductID", Include: true, IsPrimaryKey: true),
                        new ColumnSelectionRequest("ProductName", Include: true, IsPrimaryKey: false),
                        new ColumnSelectionRequest("UnitPrice", Include: true, IsPrimaryKey: false)
                    },
                    WriteActions: writeActions)
            }));

        var path = Path.Combine(_tempDir, $"dab-config-{Guid.NewGuid():n}.json");
        File.WriteAllText(path, configJson);
        return path;
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsForbidden_WhenEntityConfigHasNoWritePermissions()
    {
        var configPath = WriteConfigFile(writeActions: null); // Read-only

        var instance = new DabInstance
        {
            Id = "inst-ro",
            ConfigPath = configPath,
            Port = 7701,
            Status = DabInstanceStatus.Running
        };
        _processManager.StartInstanceForTest(instance);

        var fakeMcp = new FakeMcpClient(Array.Empty<McpTool>(), new());
        var service = new MutationService(
            _processManager,
            new FakeMcpClientFactory(fakeMcp),
            _auditStore,
            httpClient: null,
            NullLogger<MutationService>.Instance);

        var command = new ExecuteMutationCommand(
            Entity: "Products",
            Operation: "update",
            PrimaryKey: new Dictionary<string, object?> { ["ProductID"] = 1 },
            PreviousValues: null,
            Values: new Dictionary<string, object?> { ["UnitPrice"] = 25.0 });

        var result = await service.ExecuteAsync("inst-ro", command);

        Assert.False(result.Success);
        Assert.True(result.IsForbidden);
        Assert.Contains("not permitted", result.Message);

        var audits = await _auditStore.GetRecentAsync(10);
        Assert.Single(audits);
        Assert.False(audits[0].Success);
        Assert.Contains("not permitted", audits[0].ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_DispatchesViaMcp_WhenMcpToolAvailable()
    {
        var configPath = WriteConfigFile(writeActions: new[] { "update" });

        var instance = new DabInstance
        {
            Id = "inst-write",
            ConfigPath = configPath,
            Port = 7702,
            Status = DabInstanceStatus.Running
        };
        _processManager.StartInstanceForTest(instance);

        var updateTool = new McpTool(
            Name: "update_record",
            Description: "Updates an existing record",
            InputSchema: null);

        var scriptedResults = new Dictionary<string, McpToolCallResult>
        {
            ["update_record"] = new McpToolCallResult(
                Text: """{"ProductID": 1, "ProductName": "Chai", "UnitPrice": 19.99}""",
                IsError: false)
        };

        var fakeMcp = new FakeMcpClient(new[] { updateTool }, scriptedResults);
        var service = new MutationService(
            _processManager,
            new FakeMcpClientFactory(fakeMcp),
            _auditStore,
            httpClient: null,
            NullLogger<MutationService>.Instance);

        var command = new ExecuteMutationCommand(
            Entity: "Products",
            Operation: "update",
            PrimaryKey: new Dictionary<string, object?> { ["ProductID"] = 1 },
            PreviousValues: new Dictionary<string, object?> { ["UnitPrice"] = 18.0 },
            Values: new Dictionary<string, object?> { ["UnitPrice"] = 19.99 });

        var result = await service.ExecuteAsync("inst-write", command);

        Assert.True(result.Success);
        Assert.Contains("update_record", fakeMcp.CalledToolNames);

        var audits = await _auditStore.GetRecentAsync(10);
        Assert.Single(audits);
        Assert.True(audits[0].Success);
        Assert.Equal("Products", audits[0].Entity);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNotFound_WhenInstanceDoesNotExist()
    {
        var fakeMcp = new FakeMcpClient(Array.Empty<McpTool>(), new());
        var service = new MutationService(
            _processManager,
            new FakeMcpClientFactory(fakeMcp),
            _auditStore,
            httpClient: null,
            NullLogger<MutationService>.Instance);

        var command = new ExecuteMutationCommand(
            Entity: "Products",
            Operation: "update",
            PrimaryKey: null,
            PreviousValues: null,
            Values: new());

        var result = await service.ExecuteAsync("non-existent-instance", command);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }
}
