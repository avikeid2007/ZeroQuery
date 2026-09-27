using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Zeroquery.Api.Security;

public static class RateLimitingExtensions
{
    public const string QueryPolicy = "query";
    public const string IntrospectPolicy = "introspect";
    public const string InstancesPolicy = "instances";
    public const string MutationsPolicy = "mutations";

    public static IServiceCollection AddZeroqueryRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var queriesPerMinute = int.TryParse(configuration["RATE_LIMIT_QUERIES_PER_MINUTE"], out var qpm) ? qpm : 30;
        var introspectPerMinute = int.TryParse(configuration["RATE_LIMIT_INTROSPECT_PER_MINUTE"], out var ipm) ? ipm : 10;
        var instancesPerMinute = int.TryParse(configuration["RATE_LIMIT_INSTANCES_PER_MINUTE"], out var spm) ? spm : 5;
        var mutationsPerMinute = int.TryParse(configuration["RATE_LIMIT_MUTATIONS_PER_MINUTE"], out var mpm) ? mpm : 5;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync("{\"error\":\"Too many requests. Please wait before retrying.\"}", token);
            };

            options.AddPolicy(QueryPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = queriesPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy(IntrospectPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = introspectPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy(InstancesPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = instancesPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy(MutationsPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolveClientIp(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = mutationsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    public static string ResolveClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) &&
            !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var ip = forwardedFor.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(ip)) return ip;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
