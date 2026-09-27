using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Zeroquery.Api.Introspection;
using Zeroquery.Api.Mutations;
using Zeroquery.Core.Audit;
using Zeroquery.Core.Mutations;

namespace Zeroquery.Tests.Mutations;

public class MutationEndpointsTests
{
    private sealed class StubMutationService : IMutationService
    {
        public MutationResult ResultToReturn { get; set; } = new(true, "OK");
        public ExecuteMutationCommand? LastCommand { get; private set; }

        public Task<MutationResult> ExecuteAsync(
            string instanceId,
            ExecuteMutationCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class StubAuditStore : IWriteAuditStore
    {
        public List<WriteAuditEntry> Entries { get; } = new();

        public Task RecordAsync(WriteAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WriteAuditEntry>> GetRecentAsync(
            int limit = 50,
            string? entity = null,
            CancellationToken cancellationToken = default)
        {
            var result = Entries.AsEnumerable();
            if (!string.IsNullOrEmpty(entity))
            {
                result = result.Where(e => e.Entity == entity);
            }
            return Task.FromResult<IReadOnlyList<WriteAuditEntry>>(result.Take(limit).ToList());
        }
    }

    [Fact]
    public async Task MutationEndpoints_ExecuteMutationCommand_CarriesDataCorrectly()
    {
        var stubService = new StubMutationService();
        var request = new ExecuteMutationRequest(
            Entity: "Products",
            Operation: "update",
            PrimaryKey: new Dictionary<string, object?> { ["ProductID"] = 10 },
            PreviousValues: new Dictionary<string, object?> { ["UnitPrice"] = 15.0 },
            Values: new Dictionary<string, object?> { ["UnitPrice"] = 20.0 });

        var command = request.ToCommand("127.0.0.1");
        var result = await stubService.ExecuteAsync("inst-1", command);

        Assert.True(result.Success);
        Assert.NotNull(stubService.LastCommand);
        Assert.Equal("Products", stubService.LastCommand.Entity);
        Assert.Equal("update", stubService.LastCommand.Operation);
        Assert.Equal("127.0.0.1", stubService.LastCommand.ClientIp);
        Assert.Equal(20.0, stubService.LastCommand.Values["UnitPrice"]);
    }

    [Fact]
    public async Task AuditStore_RecordsAndQueriesEntries()
    {
        var stubStore = new StubAuditStore();
        await stubStore.RecordAsync(new WriteAuditEntry(
            Id: "audit-1",
            Timestamp: DateTimeOffset.UtcNow,
            ClientIp: "192.168.1.1",
            InstanceId: "inst-1",
            Entity: "Orders",
            Operation: "create",
            PrimaryKey: null,
            PreviousValues: null,
            NewValues: new Dictionary<string, object?> { ["OrderId"] = 101 },
            Success: true));

        var entries = await stubStore.GetRecentAsync();
        Assert.Single(entries);
        Assert.Equal("Orders", entries[0].Entity);
        Assert.Equal("create", entries[0].Operation);
    }
}
