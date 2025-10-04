using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Grafirio.Shared.Services;

namespace Grafirio.Shared.Extensions
{
    public static class CompanyAuthorizationExt
    {
        /// <summary>
        /// Adds company-based authorization to endpoint group.
        /// Checks if user has access to the company specified in route or query.
        /// </summary>
        public static RouteGroupBuilder RequireCompanyAccess(this RouteGroupBuilder builder)
        {
            return builder.AddEndpointFilter(async (context, next) =>
            {
                var httpContext = context.HttpContext;
                var identityService = httpContext.RequestServices.GetRequiredService<IIdentityService>();

                // Try to get companyId from route parameters
                var companyId = GetCompanyIdFromRequest(httpContext);
                
                if (companyId.HasValue)
                {
                    if (!identityService.HasCompanyAccess(companyId.Value))
                    {
                        return Results.Forbid();
                    }
                }

                return await next(context);
            });
        }

        /// <summary>
        /// Requires specific business role for the endpoint
        /// </summary>
        public static RouteGroupBuilder RequireBusinessRole(this RouteGroupBuilder builder, string role)
        {
            return builder.AddEndpointFilter(async (context, next) =>
            {
                var httpContext = context.HttpContext;
                var identityService = httpContext.RequestServices.GetRequiredService<IIdentityService>();
                var companyId = GetCompanyIdFromRequest(httpContext);

                if (!identityService.HasBusinessRole(role, companyId))
                {
                    return Results.Forbid();
                }

                return await next(context);
            });
        }

        /// <summary>
        /// Requires company admin role
        /// </summary>
        public static RouteGroupBuilder RequireCompanyAdmin(this RouteGroupBuilder builder)
        {
            return builder.RequireBusinessRole("COMPANY_ADMIN");
        }

        /// <summary>
        /// Requires company manager or admin role
        /// </summary>
        public static RouteGroupBuilder RequireCompanyManager(this RouteGroupBuilder builder)
        {
            return builder.AddEndpointFilter(async (context, next) =>
            {
                var httpContext = context.HttpContext;
                var identityService = httpContext.RequestServices.GetRequiredService<IIdentityService>();
                var companyId = GetCompanyIdFromRequest(httpContext);

                if (!identityService.HasBusinessRole("COMPANY_ADMIN", companyId) &&
                    !identityService.HasBusinessRole("COMPANY_MANAGER", companyId))
                {
                    return Results.Forbid();
                }

                return await next(context);
            });
        }

        private static Guid? GetCompanyIdFromRequest(HttpContext httpContext)
        {
            // Try route parameters first
            if (httpContext.Request.RouteValues.TryGetValue("companyId", out var routeCompanyId))
            {
                if (Guid.TryParse(routeCompanyId?.ToString(), out var parsedRouteId))
                {
                    return parsedRouteId;
                }
            }

            // Try query parameters
            if (httpContext.Request.Query.TryGetValue("companyId", out var queryCompanyId))
            {
                if (Guid.TryParse(queryCompanyId.FirstOrDefault(), out var parsedQueryId))
                {
                    return parsedQueryId;
                }
            }

            // Try headers (for some APIs)
            if (httpContext.Request.Headers.TryGetValue("X-Company-Id", out var headerCompanyId))
            {
                if (Guid.TryParse(headerCompanyId.FirstOrDefault(), out var parsedHeaderId))
                {
                    return parsedHeaderId;
                }
            }

            return null;
        }
    }
}