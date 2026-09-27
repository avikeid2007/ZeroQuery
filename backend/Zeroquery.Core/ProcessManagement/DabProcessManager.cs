using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zeroquery.Core.ProcessManagement;

/// <summary>
/// Spawns, tracks, and reaps <c>dab start</c> subprocesses — one per active user database
/// connection (doc/Plan.md Section 2.4). Registered as a singleton; the internal registry is
/// a <see cref="ConcurrentDictionary{TKey,TValue}"/> guarded per-instance by each
/// <see cref="DabInstance"/> not being mutated concurrently in practice (start/stop are
/// serialized per id via the dictionary's atomic add/remove).
///
/// Responsibilities kept deliberately narrow here — this class does NOT:
/// - Decide when to start an instance (that's an API/orchestration concern)
/// - Proxy queries to the instance (future orchestration layer; call <see cref="TouchInstance"/>
///   whenever a request is proxied so idle-timeout tracking stays accurate)
/// </summary>
public sealed class DabProcessManager : IDisposable
{
    private readonly ConcurrentDictionary<string, DabInstance> _instances = new();
    private readonly DabProcessManagerOptions _options;
    private readonly ILogger<DabProcessManager> _logger;

    public DabProcessManager(IOptions<DabProcessManagerOptions> options, ILogger<DabProcessManager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Snapshot of all tracked instances (running, starting, idle, stopped, or errored).</summary>
    public IReadOnlyCollection<DabInstance> Instances => _instances.Values.ToList();

    /// <summary>
    /// Starts a new DAB subprocess against <paramref name="configPath"/>.
    /// </summary>
    /// <param name="configPath">Absolute path to a validated dab-config.json.</param>
    /// <param name="environmentVariables">
    /// Env vars to set on the child process — must include whatever variable name the config's
    /// <c>@env('NAME')</c> connection-string reference expects. Zeroquery never logs these.
    /// </param>
    /// <exception cref="DabProcessManagerException">
    /// Thrown if the concurrent instance cap is reached or no free port is available.
    /// </exception>
    public DabInstance StartInstance(string configPath, IReadOnlyDictionary<string, string> environmentVariables)
    {
        var runningCount = _instances.Values.Count(i =>
            i.Status is DabInstanceStatus.Provisioning or DabInstanceStatus.Starting
                or DabInstanceStatus.Running or DabInstanceStatus.Idle);

        if (runningCount >= _options.MaxConcurrentInstances)
        {
            throw new DabProcessManagerException(
                $"Maximum concurrent DAB instances ({_options.MaxConcurrentInstances}) reached. " +
                "Disconnect an existing database or wait for an idle instance to be reclaimed.");
        }

        var excludePorts = _instances.Values.Select(i => i.Port).ToHashSet();
        var port = FreePortFinder.FindFreePort(_options.PortRangeStart, _options.PortRangeEnd, excludePorts);

        var instance = new DabInstance
        {
            Id = Guid.NewGuid().ToString("n"),
            ConfigPath = configPath,
            Port = port,
            Status = DabInstanceStatus.Provisioning
        };

        if (!_instances.TryAdd(instance.Id, instance))
        {
            // Effectively unreachable (fresh GUID key) but keeps the dictionary contract honest.
            throw new DabProcessManagerException("Failed to register new DAB instance (id collision).");
        }

        try
        {
            LaunchProcess(instance, environmentVariables);
        }
        catch (Exception ex)
        {
            instance.Status = DabInstanceStatus.Error;
            instance.LastError = ex.Message;
            _logger.LogError(ex, "Failed to launch DAB instance {InstanceId} on port {Port}.", instance.Id, port);
            throw new DabProcessManagerException($"Failed to start DAB process: {ex.Message}", ex);
        }

        return instance;
    }

    private void LaunchProcess(DabInstance instance, IReadOnlyDictionary<string, string> environmentVariables)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.DabExecutablePath,
            Arguments = $"start --config \"{instance.ConfigPath}\" --no-https-redirect",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["ASPNETCORE_URLS"] = $"http://localhost:{instance.Port}";
        foreach (var (key, value) in environmentVariables)
        {
            startInfo.Environment[key] = value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                instance.LastError = e.Data;
            }
        };

        process.Exited += (_, _) => OnProcessExited(instance);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        instance.Process = process;
        instance.Status = DabInstanceStatus.Starting;

        _ = WaitForStartupAsync(instance);
    }

    private void OnProcessExited(DabInstance instance)
    {
        // A process that exits while we still think it's healthy means it crashed —
        // surface that as Error rather than silently leaving stale Running/Idle state.
        if (instance.Status is DabInstanceStatus.Starting or DabInstanceStatus.Running or DabInstanceStatus.Idle)
        {
            instance.Status = DabInstanceStatus.Error;
            _logger.LogWarning(
                "DAB instance {InstanceId} on port {Port} exited unexpectedly. Last stderr: {LastError}",
                instance.Id, instance.Port, instance.LastError);
        }
    }

    /// <summary>
    /// Polls the instance's port until it accepts TCP connections (i.e. DAB's HTTP host is
    /// up) or <see cref="DabProcessManagerOptions.StartupTimeoutSeconds"/> elapses.
    /// DAB's own <c>/health</c> endpoint requires role configuration we don't want to force
    /// onto every generated config, so a plain TCP-accept probe is used as the liveness
    /// signal instead — sufficient to confirm the ASP.NET Core host bound the port.
    /// </summary>
    private async Task WaitForStartupAsync(DabInstance instance)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.StartupTimeoutSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (instance.Process?.HasExited == true)
            {
                return; // OnProcessExited already flipped status to Error.
            }

            if (await CanConnectAsync(instance.Port).ConfigureAwait(false))
            {
                instance.Status = DabInstanceStatus.Running;
                instance.LastUsedAt = DateTimeOffset.UtcNow;
                return;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        if (instance.Status == DabInstanceStatus.Starting)
        {
            instance.Status = DabInstanceStatus.Error;
            instance.LastError ??= $"DAB did not start listening on port {instance.Port} within {_options.StartupTimeoutSeconds}s.";
            _logger.LogWarning("DAB instance {InstanceId} failed to start within timeout.", instance.Id);
        }
    }

    private static async Task<bool> CanConnectAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", port).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>Looks up an instance by id, or null if unknown.</summary>
    public DabInstance? GetInstance(string id) => _instances.GetValueOrDefault(id);

    /// <summary>
    /// Test-only hook: registers a pre-built <see cref="DabInstance"/> directly into the
    /// registry, bypassing real process spawning. Used by unit tests that exercise pure
    /// status-transition logic (<see cref="RefreshStatus"/>, <see cref="TouchInstance"/>)
    /// without needing a real DAB subprocess.
    /// </summary>
    internal DabInstance StartInstanceForTest(DabInstance instance)
    {
        _instances[instance.Id] = instance;
        return instance;
    }

    /// <summary>
    /// Refreshes an instance's liveness/idle status: marks a healthy-but-stale instance
    /// Idle once it exceeds the configured idle timeout, and demotes a dead process to Error.
    /// Call before returning status to a caller so the observed state is current.
    /// </summary>
    public DabInstanceStatus RefreshStatus(DabInstance instance)
    {
        if (instance.Process?.HasExited == true && instance.Status != DabInstanceStatus.Stopped)
        {
            instance.Status = DabInstanceStatus.Error;
            return instance.Status;
        }

        if (instance.Status == DabInstanceStatus.Running)
        {
            var idleFor = DateTimeOffset.UtcNow - instance.LastUsedAt;
            if (idleFor.TotalMinutes >= _options.IdleTimeoutMinutes)
            {
                instance.Status = DabInstanceStatus.Idle;
            }
        }

        return instance.Status;
    }

    /// <summary>Marks an instance as freshly used, resetting its idle-timeout clock (and promoting Idle back to Running).</summary>
    public void TouchInstance(string id)
    {
        if (_instances.TryGetValue(id, out var instance))
        {
            instance.LastUsedAt = DateTimeOffset.UtcNow;
            if (instance.Status == DabInstanceStatus.Idle)
            {
                instance.Status = DabInstanceStatus.Running;
            }
        }
    }

    /// <summary>
    /// Stops and removes an instance (manual disconnect or idle reap). Safe to call more
    /// than once; a second call on an already-stopped/unknown id is a no-op.
    /// </summary>
    public void StopInstance(string id)
    {
        if (!_instances.TryRemove(id, out var instance))
        {
            return;
        }

        KillProcess(instance);
        instance.Status = DabInstanceStatus.Stopped;
    }

    /// <summary>Stops every tracked instance. Called on application shutdown (see doc/Plan.md "Graceful shutdown").</summary>
    public void StopAll()
    {
        foreach (var id in _instances.Keys.ToList())
        {
            StopInstance(id);
        }
    }

    private void KillProcess(DabInstance instance)
    {
        try
        {
            if (instance.Process is { HasExited: false } process)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping DAB instance {InstanceId}.", instance.Id);
        }
        finally
        {
            instance.Process?.Dispose();
        }
    }

    public void Dispose() => StopAll();
}
