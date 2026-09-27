using System.Text.Json.Nodes;
using Zeroquery.Core.Mcp;

namespace Zeroquery.Tests.Orchestration;

/// <summary>
/// Scripted <see cref="IMcpClient"/> for testing <see cref="Zeroquery.Core.Orchestration.OrchestrationService"/>
/// without a real DAB process. Tool call results are looked up by tool name from a fixed
/// dictionary supplied at construction.
/// </summary>
public sealed class FakeMcpClient : IMcpClient
{
    private readonly IReadOnlyList<McpTool> _tools;
    private readonly Dictionary<string, McpToolCallResult> _toolResults;

    public List<string> CalledToolNames { get; } = new();
    public bool WasInitialized { get; private set; }

    public FakeMcpClient(IReadOnlyList<McpTool> tools, Dictionary<string, McpToolCallResult> toolResults)
    {
        _tools = tools;
        _toolResults = toolResults;
    }

    public Task InitializeAsync(string clientName, string clientVersion, CancellationToken cancellationToken = default)
    {
        WasInitialized = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_tools);

    public Task<McpToolCallResult> CallToolAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken = default)
    {
        CalledToolNames.Add(toolName);

        if (!_toolResults.TryGetValue(toolName, out var result))
        {
            throw new McpClientException($"FakeMcpClient has no scripted result for tool '{toolName}'.");
        }

        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Factory that always returns the same pre-built <see cref="FakeMcpClient"/> instance.</summary>
public sealed class FakeMcpClientFactory : IMcpClientFactory
{
    private readonly FakeMcpClient _client;

    public FakeMcpClientFactory(FakeMcpClient client) => _client = client;

    public IMcpClient Create(string baseUrl) => _client;
}
