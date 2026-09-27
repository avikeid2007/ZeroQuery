namespace Zeroquery.Core.ProcessManagement;

/// <summary>
/// Lifecycle states for a DAB subprocess instance, matching doc/Plan.md Section 2.4
/// ("Status states, exposed via GET /api/instances/{id}/status").
/// </summary>
public enum DabInstanceStatus
{
    /// <summary>Config validated, subprocess spawn requested, port not yet confirmed.</summary>
    Provisioning,

    /// <summary>Process running, waiting on first successful health check.</summary>
    Starting,

    /// <summary>Healthy, actively serving requests (or served one within the idle window).</summary>
    Running,

    /// <summary>Healthy but no requests within the idle window; counting down to reap.</summary>
    Idle,

    /// <summary>Reaped (idle timeout) or manually disconnected by the user.</summary>
    Stopped,

    /// <summary>Process exited unexpectedly or failed health checks.</summary>
    Error
}
