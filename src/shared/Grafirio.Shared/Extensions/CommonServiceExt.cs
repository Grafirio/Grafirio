using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Grafirio.Shared.Services;
using Grafirio.Shared.Authorization;

namespace Grafirio.Shared.Extensions
{
    public static class CommonServiceExt
    {
        public static IServiceCollection AddCommonServiceExt(this IServiceCollection services, Type assembly)
        {
            services.AddHttpContextAccessor();
            services.AddMediatR(x => x.RegisterServicesFromAssemblyContaining(assembly));

            services.AddFluentValidationAutoValidation();
            services.AddValidatorsFromAssemblyContaining(assembly);
            
            // Identity Services
            services.AddScoped<IIdentityService, IdentityService>();
            services.AddScoped<IKeycloakUserService, KeycloakUserService>();

            // Authorization Handlers - Scoped because they depend on IIdentityService
            services.AddScoped<IAuthorizationHandler, CompanyAccessHandler>();
            services.AddScoped<IAuthorizationHandler, BusinessRoleHandler>();

            services.AddAutoMapper(assembly);
            return services;
        }
    }
}