using Microsoft.AspNetCore.Authorization;
using Grafirio.Shared.Services;

namespace Grafirio.Shared.Authorization
{
    /// <summary>
    /// Authorization requirement to check if user has access to a specific company
    /// </summary>
    public class CompanyAccessRequirement : IAuthorizationRequirement
    {
        public Guid CompanyId { get; }

        public CompanyAccessRequirement(Guid companyId)
        {
            CompanyId = companyId;
        }
    }

    /// <summary>
    /// Authorization handler to validate company access
    /// </summary>
    public class CompanyAccessHandler : AuthorizationHandler<CompanyAccessRequirement>
    {
        private readonly IIdentityService _identityService;

        public CompanyAccessHandler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, 
            CompanyAccessRequirement requirement)
        {
            if (_identityService.HasCompanyAccess(requirement.CompanyId))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Authorization requirement for business role
    /// </summary>
    public class BusinessRoleRequirement : IAuthorizationRequirement
    {
        public string Role { get; }
        public Guid? CompanyId { get; }

        public BusinessRoleRequirement(string role, Guid? companyId = null)
        {
            Role = role;
            CompanyId = companyId;
        }
    }

    /// <summary>
    /// Authorization handler to validate business role
    /// </summary>
    public class BusinessRoleHandler : AuthorizationHandler<BusinessRoleRequirement>
    {
        private readonly IIdentityService _identityService;

        public BusinessRoleHandler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            BusinessRoleRequirement requirement)
        {
            if (_identityService.HasBusinessRole(requirement.Role, requirement.CompanyId))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
