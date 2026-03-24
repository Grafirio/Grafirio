using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace Grafirio.Shared.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Log the exception with correlation ID
            var correlationId = context.TraceIdentifier;
            
            _logger.LogError(exception, 
                "An unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}, Method: {Method}", 
                correlationId, 
                context.Request.Path, 
                context.Request.Method);

            // Determine status code and error message based on exception type
            var (statusCode, message, errorType) = exception switch
            {
                UnauthorizedAccessException => 
                    (HttpStatusCode.Unauthorized, "You are not authorized to access this resource.", "UnauthorizedAccess"),
                
                KeyNotFoundException => 
                    (HttpStatusCode.NotFound, "The requested resource was not found.", "NotFound"),
                
                ArgumentException argEx => 
                    (HttpStatusCode.BadRequest, argEx.Message, "BadRequest"),
                
                InvalidOperationException invEx => 
                    (HttpStatusCode.BadRequest, invEx.Message, "InvalidOperation"),
                
                FluentValidation.ValidationException validEx => 
                    (HttpStatusCode.BadRequest, GetValidationErrors(validEx), "ValidationError"),
                
                _ => 
                    (HttpStatusCode.InternalServerError, 
                     "An internal server error occurred. Please try again later.", 
                     "InternalServerError")
            };

            // Create error response
            var errorResponse = new ErrorResponse
            {
                StatusCode = (int)statusCode,
                Message = message,
                ErrorType = errorType,
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow,
                Path = context.Request.Path,
                Method = context.Request.Method
            };

            // Include stack trace in development
            if (IsDevelopmentEnvironment(context))
            {
                errorResponse.StackTrace = exception.StackTrace;
                errorResponse.InnerException = exception.InnerException?.Message;
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, options));
        }

        private static string GetValidationErrors(FluentValidation.ValidationException validationException)
        {
            var errors = validationException.Errors
                .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
                .ToList();

            return string.Join("; ", errors);
        }

        private static bool IsDevelopmentEnvironment(HttpContext context)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            return environment?.Equals("Development", StringComparison.OrdinalIgnoreCase) ?? false;
        }

        private class ErrorResponse
        {
            public int StatusCode { get; set; }
            public string Message { get; set; } = string.Empty;
            public string ErrorType { get; set; } = string.Empty;
            public string CorrelationId { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public string Path { get; set; } = string.Empty;
            public string Method { get; set; } = string.Empty;
            public string? StackTrace { get; set; }
            public string? InnerException { get; set; }
        }
    }
}
