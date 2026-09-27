namespace Zeroquery.Core.ProcessManagement;

/// <summary>
/// Deployment-configurable settings for the DAB Process Manager (doc/Plan.md Section 4).
/// Bind from configuration section "DabProcessManager" (env vars: e.g.
/// <c>DabProcessManager__MaxConcurrentInstances</c>, or the documented
/// <c>MCP_MAX_CONCURRENT_INSTANCES</c> / <c>MCP_IDLE_TIMEOUT_MINUTES</c> names via explicit
/// mapping in Program.cs).
/// </summary>
public sealed class DabProcessManagerOptions
{
    /// <summary>Max concurrent DAB subprocesses per host. Additional start requests queue.</summary>
    public int MaxConcurrentInstances { get; set; } = 10;

    /// <summary>Minutes an instance can sit unused before it's reaped.</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>How often the idle-reaper background service sweeps for idle/expired instances.</summary>
    public int ReapSweepIntervalSeconds { get; set; } = 60;

    /// <summary>How long to wait for a newly spawned subprocess to start responding before marking it Error.</summary>
    public int StartupTimeoutSeconds { get; set; } = 30;

    /// <summary>Inclusive low end of the TCP port range candidate instances are allocated from.</summary>
    public int PortRangeStart { get; set; } = 6000;

    /// <summary>Inclusive high end of the TCP port range candidate instances are allocated from.</summary>
    public int PortRangeEnd { get; set; } = 6999;

    /// <summary>Executable used to launch DAB. Defaults to the "dab" CLI on PATH.</summary>
    public string DabExecutablePath { get; set; } = "dab";
}
