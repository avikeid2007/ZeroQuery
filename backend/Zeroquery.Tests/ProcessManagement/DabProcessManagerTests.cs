using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.ProcessManagement;

namespace Zeroquery.Tests.ProcessManagement;

public class DabProcessManagerTests : IDisposable
{
    private readonly List<DabProcessManager> _managersToDispose = new();

    private DabProcessManager CreateManager(Action<DabProcessManagerOptions>? configure = null)
    {
        var options = new DabProcessManagerOptions
        {
            PortRangeStart = 21000,
            PortRangeEnd = 21099,
            StartupTimeoutSeconds = 3
        };
        configure?.Invoke(options);

        var manager = new DabProcessManager(Options.Create(options), NullLogger<DabProcessManager>.Instance);
        _managersToDispose.Add(manager);
        return manager;
    }

    [Fact]
    public void StartInstance_Throws_WhenConcurrentCapReached()
    {
        var manager = CreateManager(o => o.MaxConcurrentInstances = 0);

        var ex = Assert.Throws<DabProcessManagerException>(
            () => manager.StartInstance("C:\\fake\\dab-config.json", new Dictionary<string, string>()));

        Assert.Contains("Maximum concurrent", ex.Message);
    }

    [Fact]
    public void GetInstance_ReturnsNull_ForUnknownId()
    {
        var manager = CreateManager();

        Assert.Null(manager.GetInstance("does-not-exist"));
    }

    [Fact]
    public void StopInstance_IsIdempotent_ForUnknownId()
    {
        var manager = CreateManager();

        // Should not throw even though nothing was ever started.
        manager.StopInstance("does-not-exist");
        manager.StopInstance("does-not-exist");
    }

    [Fact]
    public void StartInstance_WithNonExistentExecutable_MarksInstanceError()
    {
        var manager = CreateManager(o => o.DabExecutablePath = "this-binary-does-not-exist-zq");

        Assert.Throws<DabProcessManagerException>(
            () => manager.StartInstance("C:\\fake\\dab-config.json", new Dictionary<string, string>()));
    }

    [Fact]
    public void RefreshStatus_PromotesRunningToIdle_AfterTimeoutElapsed()
    {
        var manager = CreateManager(o => o.IdleTimeoutMinutes = 0); // anything is "idle" instantly

        // Use a fake instance directly since we're testing pure status-transition logic,
        // not real process spawning.
        var instance = new DabInstance
        {
            Id = "test-instance",
            ConfigPath = "unused",
            Port = 21050,
            Status = DabInstanceStatus.Running,
            LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var status = manager.RefreshStatus(instance);

        Assert.Equal(DabInstanceStatus.Idle, status);
    }

    [Fact]
    public void RefreshStatus_KeepsRunning_WithinIdleWindow()
    {
        var manager = CreateManager(o => o.IdleTimeoutMinutes = 30);

        var instance = new DabInstance
        {
            Id = "test-instance",
            ConfigPath = "unused",
            Port = 21051,
            Status = DabInstanceStatus.Running,
            LastUsedAt = DateTimeOffset.UtcNow
        };

        var status = manager.RefreshStatus(instance);

        Assert.Equal(DabInstanceStatus.Running, status);
    }

    [Fact]
    public void TouchInstance_PromotesIdleBackToRunning_AndResetsClock()
    {
        var manager = CreateManager();
        var instance = manager.StartInstanceForTest(new DabInstance
        {
            Id = "touch-test",
            ConfigPath = "unused",
            Port = 21052,
            Status = DabInstanceStatus.Idle,
            LastUsedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });

        manager.TouchInstance(instance.Id);

        Assert.Equal(DabInstanceStatus.Running, instance.Status);
        Assert.True(instance.LastUsedAt > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    public void Dispose()
    {
        foreach (var manager in _managersToDispose)
        {
            manager.Dispose();
        }
    }
}
