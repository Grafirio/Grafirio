using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Grafirio.Shared.Middleware
{
    /// <summary>
    /// Middleware to log all incoming HTTP requests and responses with timing
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Skip logging for health check endpoints to reduce noise
            if (context.Request.Path.StartsWithSegments("/health") || 
                context.Request.Path.StartsWithSegments("/swagger"))
            {
                await _next(context);
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var requestTime = DateTime.UtcNow;

            // Log request
            _logger.LogInformation(
                "HTTP {Method} {Path} started at {RequestTime}",
                context.Request.Method,
                context.Request.Path,
                requestTime);

            try
            {
                await _next(context);

                stopwatch.Stop();

                // Log response
                var logLevel = context.Response.StatusCode >= 500 
                    ? LogLevel.Error 
                    : context.Response.StatusCode >= 400 
                        ? LogLevel.Warning 
                        : LogLevel.Information;

                _logger.Log(
                    logLevel,
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                _logger.LogError(
                    ex,
                    "HTTP {Method} {Path} failed after {ElapsedMilliseconds}ms",
                    context.Request.Method,
                    context.Request.Path,
                    stopwatch.ElapsedMilliseconds);

                throw;
            }
        }
    }
}
