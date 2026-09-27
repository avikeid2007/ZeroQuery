using System.Text.Json.Nodes;

namespace Zeroquery.Core.Orchestration;

/// <summary>
/// A single tool definition offered to the LLM in OpenAI-compatible "function calling" shape
/// (mirrors <c>{"type":"function","function":{"name","description","parameters"}}</c>).
/// </summary>
public sealed record LlmTool(string Name, string? Description, JsonNode? ParametersSchema);

/// <summary>A tool call the LLM asked the orchestrator to execute.</summary>
public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>
/// One turn of conversation history sent back to the LLM. <see cref="ToolCallId"/> is set
/// only for "tool" role messages (the result of a previous <see cref="LlmToolCall"/>).
/// </summary>
public sealed record LlmMessage(string Role, string? Content, IReadOnlyList<LlmToolCall>? ToolCalls = null, string? ToolCallId = null)
{
    public static LlmMessage System(string content) => new("system", content);
    public static LlmMessage User(string content) => new("user", content);
    public static LlmMessage Assistant(string? content, IReadOnlyList<LlmToolCall>? toolCalls = null) => new("assistant", content, toolCalls);
    public static LlmMessage ToolResult(string toolCallId, string content) => new("tool", content, ToolCallId: toolCallId);
}

/// <summary>
/// Result of one LLM completion call: either free-text content, or one/more tool calls the
/// caller must execute and feed back via <see cref="LlmMessage.ToolResult"/>.
/// </summary>
public sealed record LlmCompletion(string? Content, IReadOnlyList<LlmToolCall> ToolCalls);

/// <summary>
/// Abstraction over an OpenAI-compatible chat completion API with tool-calling support
/// (doc/Plan.md Section 2.7 — "OpenRouter/OpenAI-compatible via ILlmProvider abstraction").
/// Implementations are expected to be stateless/reusable across requests.
/// </summary>
public interface ILlmProvider
{
    Task<LlmCompletion> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool> tools,
        CancellationToken cancellationToken = default);
}
