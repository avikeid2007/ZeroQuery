using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Introspection;
using Zeroquery.Core.Security.Ssrf;

namespace Zeroquery.Tests.Security;

public class SsrfValidatorTests
{
    private static SsrfValidator CreateValidator(bool blockPrivateNetworks = true)
    {
        var options = Options.Create(new SsrfOptions { BlockPrivateNetworks = blockPrivateNetworks });
        return new SsrfValidator(options, NullLogger<SsrfValidator>.Instance);
    }

    [Theory]
    [InlineData("Server=127.0.0.1;Database=test;", "127.0.0.1")]
    [InlineData("Data Source=tcp:127.0.0.1,1433;Initial Catalog=test;", "127.0.0.1")]
    [InlineData("Server=localhost\\SQLEXPRESS;Database=test;", "localhost")]
    [InlineData("Host=192.168.1.1;Port=5432;Database=test;", "192.168.1.1")]
    [InlineData("postgresql://user:pass@10.0.0.5:5432/mydb", "10.0.0.5")]
    [InlineData("Server=169.254.169.254;Port=3306;Database=test;", "169.254.169.254")]
    [InlineData("Server=[::1]:1433;Database=test;", "::1")]
    [InlineData("Data Source=mydb.publicdomain.com;Initial Catalog=test;", "mydb.publicdomain.com")]
    [InlineData("Data Source=(localdb)\\MSSQLLocalDB;Database=Northwinds;", "(localdb)")]
    public void ExtractHost_ParsesHostCorrectly(string connectionString, string expectedHost)
    {
        var host = SsrfValidator.ExtractHost(connectionString);
        Assert.Equal(expectedHost, host);
    }

    [Theory]
    [InlineData("Server=127.0.0.1;Database=test;")]
    [InlineData("Server=localhost;Database=test;")]
    [InlineData("Server=(local);Database=test;")]
    [InlineData("Server=(localdb);Database=test;")]
    [InlineData("Data Source=(localdb)\\MSSQLLocalDB;Database=Northwinds;")]
    [InlineData("Server=.;Database=test;")]
    [InlineData("Server=::1;Database=test;")]
    [InlineData("Server=10.0.1.50;Database=test;")]
    [InlineData("Server=172.16.0.1;Database=test;")]
    [InlineData("Server=172.31.255.255;Database=test;")]
    [InlineData("Server=192.168.1.100;Database=test;")]
    [InlineData("Server=169.254.169.254;Database=test;")] // Cloud metadata IMDS
    [InlineData("Server=169.254.1.1;Database=test;")]
    [InlineData("Server=100.64.0.1;Database=test;")] // CGNAT
    [InlineData("Server=0.0.0.0;Database=test;")]
    [InlineData("postgresql://user:pass@127.0.0.1:5432/mydb")]
    [InlineData("Server=np:\\\\.\\pipe\\sql\\query;")]
    public async Task ValidateConnectionStringAsync_BlocksPrivateOrLoopbackAddresses(string connectionString)
    {
        var validator = CreateValidator(blockPrivateNetworks: true);

        var ex = await Assert.ThrowsAsync<SsrfException>(
            () => validator.ValidateConnectionStringAsync(DatabaseProvider.SqlServer, connectionString));

        Assert.NotEmpty(ex.Message);
        Assert.Contains("SSRF protection", ex.Message);
    }

    [Fact]
    public async Task ValidateConnectionStringAsync_AllowsPublicIpAddress()
    {
        var validator = CreateValidator(blockPrivateNetworks: true);

        // 93.184.216.34 is example.com's public IP
        await validator.ValidateConnectionStringAsync(
            DatabaseProvider.SqlServer, "Server=93.184.216.34;Database=test;");
    }

    [Fact]
    public async Task ValidateConnectionStringAsync_AllowsPrivate_WhenBlockPrivateNetworksIsFalse()
    {
        var validator = CreateValidator(blockPrivateNetworks: false);

        // Should not throw even for 127.0.0.1, (localdb), or 169.254.169.254 when explicitly configured
        await validator.ValidateConnectionStringAsync(
            DatabaseProvider.SqlServer, "Server=127.0.0.1;Database=test;");
        await validator.ValidateConnectionStringAsync(
            DatabaseProvider.SqlServer, "Data Source=(localdb)\\MSSQLLocalDB;Database=Northwinds;");
        await validator.ValidateConnectionStringAsync(
            DatabaseProvider.SqlServer, "Server=169.254.169.254;Database=test;");
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.5.5", true)]
    [InlineData("172.31.0.1", true)]
    [InlineData("172.32.0.1", false)] // Public range
    [InlineData("192.168.0.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("100.64.1.1", true)]
    [InlineData("8.8.8.8", false)] // Public DNS
    [InlineData("1.1.1.1", false)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)] // Link-local IPv6
    [InlineData("fc00::1", true)] // Unique-local IPv6
    public void IsPrivateOrRestricted_AccuratelyIdentifiesIpRanges(string ipString, bool expectedRestricted)
    {
        var ip = IPAddress.Parse(ipString);
        var result = SsrfValidator.IsPrivateOrRestricted(ip);
        Assert.Equal(expectedRestricted, result);
    }

    [Fact]
    public void IsPrivateOrRestricted_HandlesIPv4MappedIPv6Address()
    {
        var mappedLoopback = IPAddress.Parse("::ffff:127.0.0.1");
        Assert.True(SsrfValidator.IsPrivateOrRestricted(mappedLoopback));

        var mappedPublic = IPAddress.Parse("::ffff:8.8.8.8");
        Assert.False(SsrfValidator.IsPrivateOrRestricted(mappedPublic));
    }
}
