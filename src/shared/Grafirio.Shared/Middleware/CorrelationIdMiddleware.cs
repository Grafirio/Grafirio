using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Grafirio.Shared.Middleware
{
    /// <summary>
    /// Middleware to add correlation ID to each request for distributed tracing
    /// </summary>
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;
        private const string CorrelationIdHeader = "X-Correlation-Id";

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Get or generate correlation ID
            var correlationId = GetOrGenerateCorrelationId(context);

            // Add to response headers
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
                {
                    context.Response.Headers[CorrelationIdHeader] = correlationId;
                }
                return Task.CompletedTask;
            });

            // Add to logger scope
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["RequestPath"] = context.Request.Path,
                ["RequestMethod"] = context.Request.Method
            }))
            {
                await _next(context);
            }
        }

        private static string GetOrGenerateCorrelationId(HttpContext context)
        {
            // Check if correlation ID exists in request headers
            if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId) 
                && !string.IsNullOrWhiteSpace(correlationId))
            {
                return correlationId.ToString();
            }

            // Generate new correlation ID
            return Guid.NewGuid().ToString();
        }
    }
}
