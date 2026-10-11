using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Server.RateLimiting;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    // Requests allowed per client IP in each window.
    public int PermitLimit { get; init; } = 60;

    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);
}

// Limits requests per client IP on the business API, so nobody burns the free OpenDota quota
// (shared by every user) or the hosting quota. The client IP comes from X-Forwarded-For only
// when forwarded headers are enabled (behind the platform's proxy).
public static class ApiRateLimiting
{
    public const string PolicyName = "api";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(options => options.PermitLimit > 0, "RateLimiting:PermitLimit must be positive.")
            .Validate(options => options.Window > TimeSpan.Zero, "RateLimiting:Window must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(PolicyName, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientKey(httpContext.Connection.RemoteIpAddress),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = options.Window,
                        // Reject right away instead of making the client wait in a queue.
                        QueueLimit = 0,
                    });
            });

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    httpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new()
                {
                    HttpContext = httpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Too many requests from this IP. Please wait a moment and try again.",
                    },
                });
            };
        });

        return services;
    }

    // The key each client is counted by. An IPv6 user usually controls a whole /64 network, so counting
    // single IPv6 addresses would let them get a fresh limit on every request by changing address.
    internal static string ClientKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8); // keep only the /64 network prefix
        return $"{new IPAddress(bytes)}/64";
    }
}
