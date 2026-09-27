using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Zeroquery.Core.ProcessManagement.ResourceLimits;

/// <summary>
/// Cross-platform process resource limiter that uses Win32 Job Objects on Windows
/// and process priority tuning on Linux/macOS.
/// </summary>
public sealed class ProcessResourceLimiter : IProcessResourceLimiter
{
    private readonly IProcessResourceLimiter? _windowsLimiter;
    private readonly ILogger<ProcessResourceLimiter> _logger;

    public ProcessResourceLimiter(ILogger<ProcessResourceLimiter> logger)
    {
        _logger = logger;
        if (OperatingSystem.IsWindows())
        {
            _windowsLimiter = new WindowsJobObjectLimiter(logger);
        }
    }

    public void ApplyLimits(Process process, int maxMemoryMb, int cpuLimitPercent)
    {
        if (process.HasExited) return;

        try
        {
            if (OperatingSystem.IsWindows() && _windowsLimiter is not null)
            {
                _windowsLimiter.ApplyLimits(process, maxMemoryMb, cpuLimitPercent);
                return;
            }

            // Fallback for Linux/macOS: lower execution priority
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
            _logger.LogInformation("Applied BelowNormal priority to subprocess {ProcessId}.", process.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply resource limits to subprocess {ProcessId}.", process.Id);
        }
    }

    public void Dispose()
    {
        _windowsLimiter?.Dispose();
    }
}
