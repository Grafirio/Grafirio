using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Grafirio.DataAnalysis.Api.Api.Extensions;

public static class AnalysisRequestLimits
{
    private const int RequestsPerMinute = 12;
    private const int MaxConcurrentRequests = 2;

    public static IServiceCollection AddAnalysisRequestLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsExpensive(context)
                    ? RateLimitPartition.GetFixedWindowLimiter(Key(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                    })
                    : RateLimitPartition.GetNoLimiter("unlimited")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsExpensive(context)
                    ? RateLimitPartition.GetConcurrencyLimiter(Key(context), _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = MaxConcurrentRequests, QueueLimit = 0
                    })
                    : RateLimitPartition.GetNoLimiter("unlimited")));
        });
        return services;
    }

    private static bool IsExpensive(HttpContext context) => HttpMethods.IsPost(context.Request.Method)
        && (context.Request.Path.StartsWithSegments("/api/agent")
            || context.Request.Path.StartsWithSegments("/api/analysis"));

    private static string Key(HttpContext context) => context.User.FindFirst("company_id")?.Value
        ?? context.User.FindFirst("sub")?.Value
        ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
}