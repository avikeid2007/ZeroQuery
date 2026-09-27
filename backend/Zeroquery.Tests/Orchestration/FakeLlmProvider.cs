using Zeroquery.Core.Orchestration;

namespace Zeroquery.Tests.Orchestration;

/// <summary>
/// Scripted <see cref="ILlmProvider"/> for testing <see cref="OrchestrationService"/>'s
/// tool-calling loop without a real LLM. Returns queued <see cref="LlmCompletion"/>s in
/// order, one per call to <see cref="CompleteAsync"/>, and records every message list it
/// was given so tests can assert on conversation history (e.g. tool results were fed back).
/// </summary>
public sealed class FakeLlmProvider : ILlmProvider
{
    private readonly Queue<LlmCompletion> _scriptedCompletions;

    public List<IReadOnlyList<LlmMessage>> RecordedCalls { get; } = new();
    public List<IReadOnlyList<LlmTool>> RecordedTools { get; } = new();
    public IReadOnlyList<LlmTool>? LastTools => RecordedTools.LastOrDefault();

    public FakeLlmProvider(IEnumerable<LlmCompletion> scriptedCompletions)
    {
        _scriptedCompletions = new Queue<LlmCompletion>(scriptedCompletions);
    }

    public Task<LlmCompletion> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool> tools,
        CancellationToken cancellationToken = default)
    {
        RecordedCalls.Add(messages);
        RecordedTools.Add(tools);

        if (_scriptedCompletions.Count == 0)
        {
            throw new InvalidOperationException("FakeLlmProvider ran out of scripted completions.");
        }

        return Task.FromResult(_scriptedCompletions.Dequeue());
    }
}
