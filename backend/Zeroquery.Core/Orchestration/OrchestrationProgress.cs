namespace Zeroquery.Core.Orchestration;

/// <summary>
/// A stage in <see cref="OrchestrationService"/>'s tool-calling loop, reported via
/// <see cref="OrchestrationProgressCallback"/> so callers (e.g. the SSE query-stream
/// endpoint, doc/Plan.md Phase 5) can show live progress instead of a static spinner while
/// the LLM thinks and calls MCP tools.
/// </summary>
public enum OrchestrationStage
{
    /// <summary>Waiting on the LLM provider for its next completion.</summary>
    Thinking,

    /// <summary>About to execute an MCP tool call the LLM requested.</summary>
    ToolCall,

    /// <summary>An MCP tool call finished and its result is being fed back to the LLM.</summary>
    ToolResult,

    /// <summary>The LLM called <c>render_result</c> — the final UI Spec is being parsed.</summary>
    Rendering
}

/// <summary>One reported progress event. <see cref="ToolName"/> is set only for the two tool-call stages.</summary>
public sealed record OrchestrationProgressEvent(OrchestrationStage Stage, string? ToolName = null);

/// <summary>
/// Callback invoked as <see cref="OrchestrationService.RunQueryAsync"/> progresses through
/// its tool-calling loop. Optional — callers that only want the final result (e.g. the
/// existing non-streaming query endpoint) simply omit it.
/// </summary>
public delegate Task OrchestrationProgressCallback(OrchestrationProgressEvent progressEvent, CancellationToken cancellationToken);
