using System.Net;
using Microsoft.AspNetCore.Http;
using Zeroquery.Api.Security;

namespace Zeroquery.Tests.Security;

public class RateLimitingTests
{
    [Fact]
    public void ResolveClientIp_ExtractsFirstIpFromForwardedForHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.195, 70.41.3.18, 150.172.238.178";

        var ip = RateLimitingExtensions.ResolveClientIp(context);

        Assert.Equal("203.0.113.195", ip);
    }

    [Fact]
    public void ResolveClientIp_FallsBackToRemoteIpAddress_WhenHeaderAbsent()
    {
        var context = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse("198.51.100.42") }
        };

        var ip = RateLimitingExtensions.ResolveClientIp(context);

        Assert.Equal("198.51.100.42", ip);
    }

    [Fact]
    public void ResolveClientIp_ReturnsUnknown_WhenBothAbsent()
    {
        var context = new DefaultHttpContext();

        var ip = RateLimitingExtensions.ResolveClientIp(context);

        Assert.Equal("unknown", ip);
    }
}
