namespace Grafirio.Shared.Services
{
    public interface IIdentityService
    {
        Guid UserId { get; }
        string UserName { get; }
        string Email { get; }
        string FullName { get; }
        List<string> Roles { get; }

        // Company & Business Authorization
        Guid? CurrentCompanyId { get; }
        List<Guid> AccessibleCompanyIds { get; }
        
        // Claims Helper Methods
        string? GetClaim(string claimType);
        List<string> GetClaimValues(string claimType);
        bool HasClaim(string claimType, string? claimValue = null);
        
        // Business Authorization
        bool HasCompanyAccess(Guid companyId);
        bool HasBusinessRole(string role, Guid? companyId = null);
        List<string> GetBusinessRoles(Guid? companyId = null);
    }
}