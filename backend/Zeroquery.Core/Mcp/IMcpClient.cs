using System.Text.Json.Nodes;

namespace Zeroquery.Core.Mcp;

/// <summary>
/// Abstraction over <see cref="McpClient"/> so callers (notably
/// <see cref="Orchestration.OrchestrationService"/>) can be unit-tested against a fake MCP
/// server without a real DAB subprocess.
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
    Task InitializeAsync(string clientName, string clientVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<McpToolCallResult> CallToolAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken = default);
}

/// <summary>Creates an <see cref="IMcpClient"/> for a given DAB instance base URL.</summary>
public interface IMcpClientFactory
{
    IMcpClient Create(string baseUrl);
}

/// <summary>Default factory producing real <see cref="McpClient"/> instances.</summary>
public sealed class McpClientFactory : IMcpClientFactory
{
    public IMcpClient Create(string baseUrl) => new McpClient(baseUrl);
}
