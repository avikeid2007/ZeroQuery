using System.Net;
using System.Net.Sockets;
using Zeroquery.Core.ProcessManagement;

namespace Zeroquery.Tests.ProcessManagement;

public class FreePortFinderTests
{
    [Fact]
    public void FindFreePort_ReturnsPortWithinRange()
    {
        var port = FreePortFinder.FindFreePort(20000, 20100, new HashSet<int>());

        Assert.InRange(port, 20000, 20100);
    }

    [Fact]
    public void FindFreePort_SkipsExcludedPorts()
    {
        var exclude = new HashSet<int> { 20200 };

        var port = FreePortFinder.FindFreePort(20200, 20205, exclude);

        Assert.NotEqual(20200, port);
        Assert.InRange(port, 20201, 20205);
    }

    [Fact]
    public void FindFreePort_SkipsActuallyBoundPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 20300);
        listener.Start();

        var port = FreePortFinder.FindFreePort(20300, 20305, new HashSet<int>());

        Assert.NotEqual(20300, port);
    }

    [Fact]
    public void FindFreePort_Throws_WhenRangeFullyExcluded()
    {
        var exclude = new HashSet<int> { 20400, 20401, 20402 };

        Assert.Throws<DabProcessManagerException>(() => FreePortFinder.FindFreePort(20400, 20402, exclude));
    }
}
