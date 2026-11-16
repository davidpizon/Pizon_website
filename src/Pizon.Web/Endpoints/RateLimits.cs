using System.Threading.RateLimiting;

namespace Pizon.Web;

/// <summary>
/// Per-IP rate limits for writes.
/// </summary>
public static class RateLimits
{
    /// <summary>The policy for posts, pages and handle changes.</summary>
    public const string Writes = "writes";

    /// <summary>The policy for score posts.</summary>
    public const string Scores = "scores";

    /// <summary>
    /// Adds the rate limiter with both policies.
    /// </summary>
    /// <param name="services">The services.</param>
    public static void AddBbsRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Writes, http => PerIp(http, 8));
            options.AddPolicy(Scores, http => PerIp(http, 60));
        });
    }

    private static RateLimitPartition<string> PerIp(HttpContext http, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
