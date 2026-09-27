using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zeroquery.Core.ProcessManagement;

/// <summary>
/// Background sweep that reaps instances which have been <see cref="DabInstanceStatus.Idle"/>
/// for the configured idle timeout, freeing their port/process resources (doc/Plan.md
/// Section 2.4 — "Idle timeout: kill subprocesses unused for N minutes"). Status transitions
/// from Running -&gt; Idle happen lazily in <see cref="DabProcessManager.RefreshStatus"/> when
/// a caller checks status; this service periodically forces that refresh across every
/// tracked instance so idle instances get reaped even if nobody's actively polling them.
/// </summary>
public sealed class DabIdleReaperService : BackgroundService
{
    private readonly DabProcessManager _processManager;
    private readonly DabProcessManagerOptions _options;
    private readonly ILogger<DabIdleReaperService> _logger;

    public DabIdleReaperService(
        DabProcessManager processManager,
        IOptions<DabProcessManagerOptions> options,
        ILogger<DabIdleReaperService> logger)
    {
        _processManager = processManager;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.ReapSweepIntervalSeconds));

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            SweepOnce();
        }
    }

    private void SweepOnce()
    {
        foreach (var instance in _processManager.Instances)
        {
            var status = _processManager.RefreshStatus(instance);
            if (status != DabInstanceStatus.Idle)
            {
                continue;
            }

            var idleFor = DateTimeOffset.UtcNow - instance.LastUsedAt;
            if (idleFor.TotalMinutes < _options.IdleTimeoutMinutes)
            {
                continue;
            }

            _logger.LogInformation(
                "Reaping idle DAB instance {InstanceId} on port {Port} (idle for {IdleMinutes:F1} min).",
                instance.Id, instance.Port, idleFor.TotalMinutes);

            _processManager.StopInstance(instance.Id);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // Graceful shutdown: terminate all child DAB processes cleanly (doc/Plan.md
        // "Graceful shutdown: on app restart/deploy, terminate all child DAB processes").
        _processManager.StopAll();
        return base.StopAsync(cancellationToken);
    }
}
