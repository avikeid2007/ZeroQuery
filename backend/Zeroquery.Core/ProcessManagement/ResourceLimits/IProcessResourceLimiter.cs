using System.Diagnostics;

namespace Zeroquery.Core.ProcessManagement.ResourceLimits;

/// <summary>
/// Enforces CPU and memory limits on spawned subprocesses (doc/Plan.md Section 3 &amp; Phase 6).
/// </summary>
public interface IProcessResourceLimiter : IDisposable
{
    /// <summary>
    /// Applies CPU and memory limits to the given running process.
    /// </summary>
    /// <param name="process">The target process.</param>
    /// <param name="maxMemoryMb">Maximum memory in MB (0 = uncapped).</param>
    /// <param name="cpuLimitPercent">Maximum CPU limit percentage 1-100 (0 = uncapped).</param>
    void ApplyLimits(Process process, int maxMemoryMb, int cpuLimitPercent);
}
