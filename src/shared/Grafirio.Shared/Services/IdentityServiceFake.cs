namespace Grafirio.Shared.Services
{
    public class IdentityServiceFake : IIdentityService
    {
        public Guid UserId => Guid.Parse("332ee8cd-f3f6-49fa-92e2-5fdb188b3377");
        public string UserName => "Ahmet16";
        public string Email => "ahmet16@test.com";
        public string FullName => "Ahmet Test User";
        public List<string> Roles => ["ADMIN"];

        // Company & Business Authorization
        public Guid? CurrentCompanyId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public List<Guid> AccessibleCompanyIds => [
            Guid.Parse("11111111-1111-1111-1111-111111111111"), 
            Guid.Parse("22222222-2222-2222-2222-222222222222")
        ];

        // Claims Helper Methods
        public string? GetClaim(string claimType) => claimType switch
        {
            "email" => Email,
            "name" => FullName,
            "company_id" => CurrentCompanyId?.ToString(),
            _ => null
        };

        public List<string> GetClaimValues(string claimType) => claimType switch
        {
            "accessible_companies" => AccessibleCompanyIds.Select(x => x.ToString()).ToList(),
            "business_roles" => ["COMPANY_ADMIN", "DASHBOARD_VIEWER"],
            "roles" => Roles,
            _ => []
        };

        public bool HasClaim(string claimType, string? claimValue = null)
        {
            var claim = GetClaim(claimType);
            return claimValue == null ? claim != null : claim == claimValue;
        }

        // Business Authorization
        public bool HasCompanyAccess(Guid companyId) => AccessibleCompanyIds.Contains(companyId);

        public bool HasBusinessRole(string role, Guid? companyId = null) => 
            GetBusinessRoles(companyId).Contains(role, StringComparer.OrdinalIgnoreCase);

        public List<string> GetBusinessRoles(Guid? companyId = null) => ["COMPANY_ADMIN", "DASHBOARD_VIEWER"];
    }
}