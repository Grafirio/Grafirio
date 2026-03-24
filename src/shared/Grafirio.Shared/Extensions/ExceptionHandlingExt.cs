using Microsoft.AspNetCore.Builder;
using Grafirio.Shared.Middleware;

namespace Grafirio.Shared.Extensions
{
    /// <summary>
    /// Extension methods for adding exception handling and logging middleware
    /// </summary>
    public static class ExceptionHandlingExt
    {
        /// <summary>
        /// Adds global exception handling, correlation ID, and request logging middleware
        /// </summary>
        public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
        {
            // Order matters: Correlation ID -> Request Logging -> Exception Handling
            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseMiddleware<RequestLoggingMiddleware>();
            app.UseMiddleware<GlobalExceptionMiddleware>();
            
            return app;
        }

        /// <summary>
        /// Adds only global exception handling middleware
        /// </summary>
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        {
            app.UseMiddleware<GlobalExceptionMiddleware>();
            return app;
        }

        /// <summary>
        /// Adds correlation ID middleware for distributed tracing
        /// </summary>
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        {
            app.UseMiddleware<CorrelationIdMiddleware>();
            return app;
        }

        /// <summary>
        /// Adds request/response logging middleware
        /// </summary>
        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
        {
            app.UseMiddleware<RequestLoggingMiddleware>();
            return app;
        }
    }
}
