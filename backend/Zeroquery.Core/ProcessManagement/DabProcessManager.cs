using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.ProcessManagement.ResourceLimits;

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
    private readonly IProcessResourceLimiter _resourceLimiter;

    public DabProcessManager(
        IOptions<DabProcessManagerOptions> options,
        ILogger<DabProcessManager> logger,
        IProcessResourceLimiter? resourceLimiter = null)
    {
        _options = options.Value;
        _logger = logger;
        _resourceLimiter = resourceLimiter ?? new ProcessResourceLimiter(NullLogger<ProcessResourceLimiter>.Instance);
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
    /// <param name="clientIp">Client IP address initiating the connection (used for per-IP abuse control).</param>
    /// <exception cref="DabProcessManagerException">
    /// Thrown if the concurrent instance cap is reached or no free port is available.
    /// </exception>
    public DabInstance StartInstance(string configPath, IReadOnlyDictionary<string, string> environmentVariables, string? clientIp = null)
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

        if (!string.IsNullOrWhiteSpace(clientIp) && _options.MaxInstancesPerIp > 0)
        {
            var clientCount = _instances.Values.Count(i =>
                i.ClientIp == clientIp &&
                i.Status is DabInstanceStatus.Provisioning or DabInstanceStatus.Starting
                    or DabInstanceStatus.Running or DabInstanceStatus.Idle);

            if (clientCount >= _options.MaxInstancesPerIp)
            {
                throw new DabProcessManagerException(
                    $"Maximum concurrent DAB instances per client ({_options.MaxInstancesPerIp}) reached. " +
                    "Disconnect an existing database or wait for an idle instance to be reclaimed.");
            }
        }

        var excludePorts = _instances.Values.Select(i => i.Port).ToHashSet();
        var port = FreePortFinder.FindFreePort(_options.PortRangeStart, _options.PortRangeEnd, excludePorts);

        var instance = new DabInstance
        {
            Id = Guid.NewGuid().ToString("n"),
            ConfigPath = configPath,
            Port = port,
            ClientIp = clientIp,
            Status = DabInstanceStatus.Provisioning
        };

        try
        {
            if (File.Exists(configPath))
            {
                var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath));
                if (doc?["runtime"]?["rest"]?["enabled"] is System.Text.Json.Nodes.JsonNode restNode)
                {
                    instance.IsRestEnabled = restNode.GetValue<bool>();
                }
                if (doc?["runtime"]?["graphql"]?["enabled"] is System.Text.Json.Nodes.JsonNode gqlNode)
                {
                    instance.IsGraphqlEnabled = gqlNode.GetValue<bool>();
                }
            }
        }
        catch
        {
            // Non-fatal, defaults will apply
        }

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

    public string ResolveDabExecutablePath()
    {
        if (File.Exists(_options.DabExecutablePath))
        {
            return _options.DabExecutablePath;
        }

        // 1. Check local application directory (bundled standalone desktop worker)
        var localDllPath = Path.Combine(AppContext.BaseDirectory, "tools", "dab", "Microsoft.DataApiBuilder.dll");
        if (File.Exists(localDllPath))
        {
            return localDllPath;
        }

        var localToolPath = Path.Combine(AppContext.BaseDirectory, "tools", "dab", OperatingSystem.IsWindows() ? "dab.exe" : "dab");
        if (File.Exists(localToolPath))
        {
            return localToolPath;
        }

        var localExePath = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "dab.exe" : "dab");
        if (File.Exists(localExePath))
        {
            return localExePath;
        }

        if (string.Equals(_options.DabExecutablePath, "dab", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_options.DabExecutablePath, "dab.exe", StringComparison.OrdinalIgnoreCase))
        {
            var userToolsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet",
                "tools",
                OperatingSystem.IsWindows() ? "dab.exe" : "dab");

            if (File.Exists(userToolsPath))
            {
                return userToolsPath;
            }
        }

        return _options.DabExecutablePath;
    }

    private void LaunchProcess(DabInstance instance, IReadOnlyDictionary<string, string> environmentVariables)
    {
        var executablePath = ResolveDabExecutablePath();
        var isDll = executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        var startInfo = new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : executablePath,
            Arguments = isDll
                ? $"\"{executablePath}\" start --config \"{instance.ConfigPath}\" --no-https-redirect"
                : $"start --config \"{instance.ConfigPath}\" --no-https-redirect",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(_options.SubprocessUserName))
        {
            startInfo.UserName = _options.SubprocessUserName;
            if (OperatingSystem.IsWindows())
            {
                if (!string.IsNullOrWhiteSpace(_options.SubprocessPassword))
                {
                    startInfo.PasswordInClearText = _options.SubprocessPassword;
                }
                if (!string.IsNullOrWhiteSpace(_options.SubprocessDomain))
                {
                    startInfo.Domain = _options.SubprocessDomain;
                }
            }
            _logger.LogInformation("Launching DAB subprocess under dedicated user '{UserName}'.", _options.SubprocessUserName);
        }

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

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception w && (w.NativeErrorCode == 2 || w.Message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
                                   || ex.Message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
        {
            throw new DabProcessManagerException(
                $"Microsoft Data API builder (dab) is not installed or could not be found at '{executablePath}'. " +
                "Please install it using 'dotnet tool install -g Microsoft.DataApiBuilder' or click 'Install DAB'.", ex);
        }

        // Apply CPU and memory limits
        _resourceLimiter.ApplyLimits(process, _options.MaxMemoryMegabytes, _options.CpuLimitPercent);

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
            if (instance.Status is DabInstanceStatus.Idle or DabInstanceStatus.Starting)
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

    /// <summary>
    /// Checks whether the DAB CLI executable can be found and executed.
    /// </summary>
    public async Task<DabStatusInfo> GetDabStatusAsync(CancellationToken cancellationToken = default)
    {
        var executable = ResolveDabExecutablePath();
        try
        {
            var isDll = executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = isDll ? "dotnet" : executable,
                    Arguments = isDll ? $"\"{executable}\" --version" : "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var outputTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);

            if (proc.ExitCode == 0)
            {
                return new DabStatusInfo(IsInstalled: true, Version: output.Trim(), ExecutablePath: executable, Error: null);
            }

            return new DabStatusInfo(IsInstalled: false, Version: null, ExecutablePath: executable, Error: $"Exit code {proc.ExitCode}");
        }
        catch (Exception ex)
        {
            return new DabStatusInfo(IsInstalled: false, Version: null, ExecutablePath: executable, Error: ex.Message);
        }
    }

    /// <summary>
    /// Installs Microsoft.DataApiBuilder globally using dotnet tool install.
    /// </summary>
    public async Task<DabInstallResult> InstallDabAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "tool install -g Microsoft.DataApiBuilder",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            var outTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
            var errTask = proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await outTask.ConfigureAwait(false);
            var stderr = await errTask.ConfigureAwait(false);

            if (proc.ExitCode == 0)
            {
                _logger.LogInformation("Successfully installed Microsoft.DataApiBuilder globally: {Output}", stdout);
                return new DabInstallResult(Success: true, Message: stdout.Trim());
            }

            // If already installed, try tool update
            if (stderr.Contains("already installed", StringComparison.OrdinalIgnoreCase) ||
                stdout.Contains("already installed", StringComparison.OrdinalIgnoreCase))
            {
                using var updateProc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "tool update -g Microsoft.DataApiBuilder",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                updateProc.Start();
                var updateOutTask = updateProc.StandardOutput.ReadToEndAsync(cancellationToken);
                var updateErrTask = updateProc.StandardError.ReadToEndAsync(cancellationToken);
                await updateProc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var updateOut = await updateOutTask.ConfigureAwait(false);
                var updateErr = await updateErrTask.ConfigureAwait(false);

                if (updateProc.ExitCode == 0)
                {
                    return new DabInstallResult(Success: true, Message: updateOut.Trim());
                }

                return new DabInstallResult(Success: false, Message: $"{updateErr}\n{updateOut}".Trim());
            }

            return new DabInstallResult(Success: false, Message: $"{stderr}\n{stdout}".Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run dotnet tool install for Microsoft.DataApiBuilder.");
            return new DabInstallResult(Success: false, Message: ex.Message);
        }
    }

    public void Dispose()
    {
        StopAll();
        _resourceLimiter.Dispose();
    }
}
