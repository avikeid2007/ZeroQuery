using System.Diagnostics;

namespace Zeroquery.Core.ProcessManagement;

/// <summary>
/// Registry entry for one running (or starting/stopped) DAB subprocess instance.
/// Mutable fields (<see cref="Status"/>, <see cref="LastUsedAt"/>, <see cref="LastError"/>)
/// are only ever mutated by <see cref="DabProcessManager"/> under its internal lock — this
/// type itself has no synchronization of its own, treat it as owned by the manager.
/// </summary>
public sealed class DabInstance
{
    /// <summary>Stable identifier for this instance, generated at provisioning time.</summary>
    public required string Id { get; init; }

    /// <summary>Absolute path to the dab-config.json this instance was started from.</summary>
    public required string ConfigPath { get; init; }

    /// <summary>TCP port the DAB subprocess is bound to (via ASPNETCORE_URLS).</summary>
    public required int Port { get; init; }

    /// <summary>The underlying OS process, once started. Null while <see cref="DabInstanceStatus.Provisioning"/>.</summary>
    public Process? Process { get; set; }

    public DabInstanceStatus Status { get; set; } = DabInstanceStatus.Provisioning;

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Timestamp of the last request proxied to this instance, or its start time if none yet.</summary>
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last stderr line captured, surfaced to the user when <see cref="Status"/> is <see cref="DabInstanceStatus.Error"/>.</summary>
    public string? LastError { get; set; }

    /// <summary>Base URL of the running instance, e.g. "http://localhost:5551".</summary>
    public string BaseUrl => $"http://localhost:{Port}";
}
