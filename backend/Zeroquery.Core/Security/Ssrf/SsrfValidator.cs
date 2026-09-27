using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Introspection;

namespace Zeroquery.Core.Security.Ssrf;

/// <summary>
/// Default implementation of <see cref="ISsrfValidator"/>. Inspects database connection strings
/// and blocks internal, private, loopback, and link-local IP addresses to prevent SSRF attacks
/// against internal infrastructure (doc/Plan.md Section 3 &amp; Phase 6).
/// </summary>
public sealed class SsrfValidator : ISsrfValidator
{
    private static readonly Regex HostRegex = new(
        @"(?:^|[;\s])(?:Server|Data\s*Source|Host|Address|Addr|Network\s*Address)\s*=\s*(?<host>[^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SsrfOptions _options;
    private readonly ILogger<SsrfValidator> _logger;

    public SsrfValidator(IOptions<SsrfOptions> options, ILogger<SsrfValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task ValidateConnectionStringAsync(DatabaseProvider provider, string connectionString, CancellationToken cancellationToken = default)
    {
        return ValidateConnectionStringAsync(connectionString, cancellationToken);
    }

    public async Task ValidateConnectionStringAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (!_options.BlockPrivateNetworks)
        {
            _logger.LogWarning("SSRF protection is disabled via BlockPrivateNetworks=false. Private IP connections are permitted.");
            return;
        }

        var host = ExtractHost(connectionString);
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogDebug("No host or server specification detected in connection string.");
            return;
        }

        // Check for local pipe or file references (e.g. named pipes)
        if (host.StartsWith(@"\\") || host.StartsWith("np:", StringComparison.OrdinalIgnoreCase))
        {
            throw new SsrfException(
                "Connection to local named pipes or IPC endpoints is blocked by SSRF protection. " +
                "To allow local connections, set MCP_BLOCK_PRIVATE_NETWORKS=false in your environment or appsettings.json.");
        }

        // Immediate check for SQL Server / localhost keywords
        if (IsLocalAlias(host))
        {
            throw new SsrfException(
                $"Connection to loopback/local address '{host}' is blocked by SSRF protection. " +
                "To allow local connections, set MCP_BLOCK_PRIVATE_NETWORKS=false in your environment or appsettings.json.");
        }

        if (IPAddress.TryParse(host, out var directIp))
        {
            if (IsPrivateOrRestricted(directIp))
            {
                throw new SsrfException(
                    $"Connection to private/restricted IP address '{directIp}' is blocked by SSRF protection. " +
                    "To allow local connections, set MCP_BLOCK_PRIVATE_NETWORKS=false in your environment or appsettings.json.");
            }
            return;
        }

        // Domain name resolution
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SsrfException($"Could not resolve database host '{host}': {ex.Message}", ex);
        }

        if (addresses.Length == 0)
        {
            throw new SsrfException($"No IP addresses found for database host '{host}'.");
        }

        foreach (var address in addresses)
        {
            if (IsPrivateOrRestricted(address))
            {
                throw new SsrfException(
                    $"Connection to '{host}' ({address}) is blocked because it resolves to a private or restricted network address. " +
                    "To allow local connections, set MCP_BLOCK_PRIVATE_NETWORKS=false in your environment or appsettings.json.");
            }
        }
    }

    public static string? ExtractHost(string connectionString)
    {
        var trimmed = connectionString.Trim();

        // Check URI format (e.g., postgresql://user:pass@host:5432/db)
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("mssql://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return CleanHost(uri.Host);
            }
        }

        // Standard ADO.NET key-value pair extraction
        string? rawHost = null;
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = trimmed };
            foreach (var key in new[] { "Server", "Data Source", "Host", "Address", "Addr", "Network Address" })
            {
                if (builder.TryGetValue(key, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                {
                    rawHost = s;
                    break;
                }
            }
        }
        catch
        {
            // If DbConnectionStringBuilder fails due to special characters, fallback to regex
        }

        if (rawHost is null)
        {
            var match = HostRegex.Match(trimmed);
            if (match.Success)
            {
                rawHost = match.Groups["host"].Value;
            }
        }

        return rawHost is not null ? CleanHost(rawHost) : null;
    }

    private static string CleanHost(string raw)
    {
        var s = raw.Trim().Trim('"', '\'');

        // Preserve named pipes and UNC paths
        if (s.StartsWith(@"\\") || s.StartsWith("np:", StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }

        // Strip tcp: prefix
        if (s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            s = s[4..];
        }

        // Handle IPv6 bracket format [::1] or [::1]:5432 or "[::1]:1433"
        var openBracket = s.IndexOf('[');
        var closeBracket = s.IndexOf(']');
        if (openBracket >= 0 && closeBracket > openBracket)
        {
            return s[(openBracket + 1)..closeBracket];
        }

        // If the string is already a valid IP literal (e.g. ::1, fe80::1, 127.0.0.1), keep as is
        if (IPAddress.TryParse(s, out _))
        {
            return s;
        }

        // Strip instance name (e.g. host\instance or host/instance)
        var slashIdx = s.IndexOfAny(['\\', '/']);
        if (slashIdx >= 0)
        {
            s = s[..slashIdx];
        }

        // Strip port if separated by comma (SQL Server style: host,1433)
        var commaIdx = s.IndexOf(',');
        if (commaIdx >= 0)
        {
            s = s[..commaIdx];
        }

        // Strip port if separated by colon (host:5432), but only if exactly one colon exists (not IPv6)
        var firstColon = s.IndexOf(':');
        var lastColon = s.LastIndexOf(':');
        if (firstColon >= 0 && firstColon == lastColon)
        {
            s = s[..firstColon];
        }

        return s.Trim();
    }

    private static bool IsLocalAlias(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("localhost.localdomain", StringComparison.OrdinalIgnoreCase) ||
               host.Equals(".", StringComparison.Ordinal) ||
               host.Equals("(local)", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase) ||
               host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("127.0.0.1", StringComparison.Ordinal) ||
               host.Equals("::1", StringComparison.Ordinal);
    }

    public static bool IsPrivateOrRestricted(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // 0.0.0.0/8 (Current network / default route)
            if (bytes[0] == 0) return true;

            // 10.0.0.0/8 (Private)
            if (bytes[0] == 10) return true;

            // 100.64.0.0/10 (Shared Address Space / Carrier-Grade NAT)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;

            // 127.0.0.0/8 (Loopback)
            if (bytes[0] == 127) return true;

            // 169.254.0.0/16 (Link-Local / IMDS Cloud Metadata e.g. 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;

            // 172.16.0.0/12 (Private)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

            // 192.0.2.0/24 (TEST-NET-1)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) return true;

            // 192.168.0.0/16 (Private)
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            // 198.51.100.0/24 (TEST-NET-2)
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return true;

            // 203.0.113.0/24 (TEST-NET-3)
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return true;

            // 224.0.0.0/4 (Multicast and reserved)
            if (bytes[0] >= 224) return true;

            return false;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            // fc00::/7 (Unique Local Address)
            if ((bytes[0] & 0xFE) == 0xFC)
            {
                return true;
            }

            // ::1 loopback and :: unspecified
            if (address.Equals(IPAddress.IPv6Loopback) || address.Equals(IPAddress.IPv6None))
            {
                return true;
            }

            return false;
        }

        return false;
    }
}
