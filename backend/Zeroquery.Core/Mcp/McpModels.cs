using System.Text.Json.Nodes;

namespace Zeroquery.Core.Mcp;

/// <summary>
/// An MCP tool definition as returned by <c>tools/list</c> — name, description, and its
/// JSON Schema input shape. Passed through to the LLM's tool-calling API largely as-is.
/// </summary>
/// <param name="Name">Tool name (e.g. "read_records").</param>
/// <param name="Description">Human-readable description the LLM uses to decide when to call it.</param>
/// <param name="InputSchema">JSON Schema describing the tool's arguments object.</param>
public sealed record McpTool(string Name, string? Description, JsonNode? InputSchema);

/// <summary>
/// Result of a <c>tools/call</c> invocation. DAB's MCP tools return their payload as a
/// single text content block containing a JSON string (see doc/Plan.md — confirmed live
/// against DAB 2.0: <c>result.content[0].text</c> holds JSON-encoded tool output).
/// </summary>
/// <param name="IsError">True if the tool call itself failed (distinct from empty results).</param>
/// <param name="Text">Raw text content returned by the tool (typically JSON).</param>
public sealed record McpToolCallResult(bool IsError, string Text);

/// <summary>Thrown for MCP transport/protocol failures (bad session, malformed response, tool error).</summary>
public sealed class McpClientException : Exception
{
    public McpClientException(string message) : base(message)
    {
    }

    public McpClientException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
