using System.Net;
using System.Net.Sockets;

namespace Zeroquery.Core.ProcessManagement;

/// <summary>
/// Finds a TCP port within a configured range that isn't currently bound by anything on the
/// host (best-effort — there's an inherent TOCTOU race between checking and the DAB
/// subprocess actually binding it, mitigated by <see cref="DabProcessManager"/> retrying
/// against the next candidate port if startup fails).
/// </summary>
internal static class FreePortFinder
{
    /// <summary>
    /// Returns the first port in [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>]
    /// not present in <paramref name="excludePorts"/> and not already bound on loopback.
    /// </summary>
    public static int FindFreePort(int rangeStart, int rangeEnd, IReadOnlySet<int> excludePorts)
    {
        for (var port = rangeStart; port <= rangeEnd; port++)
        {
            if (excludePorts.Contains(port))
            {
                continue;
            }

            if (IsPortFree(port))
            {
                return port;
            }
        }

        throw new DabProcessManagerException(
            $"No free port available in range {rangeStart}-{rangeEnd}. All ports are in use.");
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
