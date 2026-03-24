using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Grafirio.Shared.Services;

namespace Grafirio.Shared.Filters
{
    /// <summary>
    /// Endpoint filter to validate company access from route parameter
    /// </summary>
    public class ValidateCompanyAccessFilter : IEndpointFilter
    {
        private readonly IIdentityService _identityService;

        public ValidateCompanyAccessFilter(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            // Try to get companyId from route parameters
            var companyIdParam = context.HttpContext.Request.RouteValues["companyId"]?.ToString();
            
            if (companyIdParam == null)
            {
                // No company ID in route, skip validation
                return await next(context);
            }

            if (!Guid.TryParse(companyIdParam, out var companyId))
            {
                return Results.BadRequest(new { error = "Invalid company ID format" });
            }

            // Validate company access
            if (!_identityService.HasCompanyAccess(companyId))
            {
                return Results.Forbid();
            }

            return await next(context);
        }
    }

    /// <summary>
    /// Extension methods for endpoint filters
    /// </summary>
    public static class EndpointFilterExtensions
    {
        /// <summary>
        /// Adds company access validation filter to endpoint
        /// </summary>
        public static RouteHandlerBuilder WithCompanyAccessValidation(this RouteHandlerBuilder builder)
        {
            return builder.AddEndpointFilter<ValidateCompanyAccessFilter>();
        }

        /// <summary>
        /// Adds business role validation filter to endpoint
        /// </summary>
        public static RouteHandlerBuilder WithBusinessRole(this RouteHandlerBuilder builder, string role)
        {
            return builder.AddEndpointFilter(async (context, next) =>
            {
                var identityService = context.HttpContext.RequestServices.GetRequiredService<IIdentityService>();
                
                if (!identityService.HasBusinessRole(role))
                {
                    return Results.Forbid();
                }

                return await next(context);
            });
        }
    }
}
