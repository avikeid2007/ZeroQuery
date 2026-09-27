namespace Zeroquery.Core.Orchestration;

/// <summary>Tuning knobs for <see cref="OrchestrationService"/>.</summary>
public sealed class OrchestrationOptions
{
    /// <summary>Hard cap on tool-call round-trips per query, to bound cost/latency if the LLM loops.</summary>
    public int MaxToolCallIterations { get; set; } = 10;
}
