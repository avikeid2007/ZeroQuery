using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.ProcessManagement;

namespace Zeroquery.Tests.ProcessManagement;

public class ProcessResourceLimitsAndSecurityTests : IDisposable
{
    private readonly List<DabProcessManager> _managersToDispose = new();

    private DabProcessManager CreateManager(Action<DabProcessManagerOptions>? configure = null)
    {
        var options = new DabProcessManagerOptions
        {
            PortRangeStart = 22000,
            PortRangeEnd = 22099,
            StartupTimeoutSeconds = 3,
            MaxConcurrentInstances = 10,
            MaxInstancesPerIp = 2,
            MaxMemoryMegabytes = 512,
            CpuLimitPercent = 50
        };
        configure?.Invoke(options);

        var manager = new DabProcessManager(Options.Create(options), NullLogger<DabProcessManager>.Instance);
        _managersToDispose.Add(manager);
        return manager;
    }

    [Fact]
    public void StartInstance_EnforcesMaxInstancesPerIp()
    {
        var manager = CreateManager(o => o.MaxInstancesPerIp = 1);

        // Simulate active instance for IP 1.2.3.4
        manager.StartInstanceForTest(new DabInstance
        {
            Id = "inst-1",
            ConfigPath = "fake",
            Port = 22001,
            ClientIp = "1.2.3.4",
            Status = DabInstanceStatus.Running
        });

        // Second instance for same IP should throw
        var ex = Assert.Throws<DabProcessManagerException>(() =>
            manager.StartInstance("fake.json", new Dictionary<string, string>(), clientIp: "1.2.3.4"));

        Assert.Contains("Maximum concurrent DAB instances per client (1) reached", ex.Message);
    }

    [Fact]
    public void StartInstance_AllowsDifferentClientIps_UnderConcurrentCap()
    {
        var manager = CreateManager(o =>
        {
            o.MaxInstancesPerIp = 1;
            o.MaxConcurrentInstances = 5;
            o.DabExecutablePath = "this-binary-does-not-exist-zq";
        });

        // Simulate active instance for IP 1.2.3.4
        manager.StartInstanceForTest(new DabInstance
        {
            Id = "inst-1",
            ConfigPath = "fake",
            Port = 22001,
            ClientIp = "1.2.3.4",
            Status = DabInstanceStatus.Running
        });

        // Different IP should NOT be blocked by per-client cap (it will fail on process launch with fake path,
        // but it will NOT throw the per-client cap exception)
        var ex = Assert.Throws<DabProcessManagerException>(() =>
            manager.StartInstance("fake.json", new Dictionary<string, string>(), clientIp: "5.6.7.8"));

        Assert.DoesNotContain("per client", ex.Message);
    }

    [Fact]
    public void Options_DefaultsMatchSecurityPolicy()
    {
        var options = new DabProcessManagerOptions();

        Assert.Equal(512, options.MaxMemoryMegabytes);
        Assert.Equal(50, options.CpuLimitPercent);
        Assert.Equal(2, options.MaxInstancesPerIp);
        Assert.Null(options.SubprocessUserName);
        Assert.Null(options.SubprocessPassword);
        Assert.Null(options.SubprocessDomain);
    }

    public void Dispose()
    {
        foreach (var manager in _managersToDispose)
        {
            manager.Dispose();
        }
        _managersToDispose.Clear();
    }
}
