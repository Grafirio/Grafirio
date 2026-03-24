using Microsoft.AspNetCore.Authorization;
using Grafirio.Shared.Services;

namespace Grafirio.Shared.Attributes
{
    /// <summary>
    /// Authorization attribute to require company access
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequireCompanyAccessAttribute : AuthorizeAttribute
    {
        public RequireCompanyAccessAttribute()
        {
            Policy = "CompanyAccess";
        }
    }

    /// <summary>
    /// Authorization attribute to require company admin role
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequireCompanyAdminAttribute : AuthorizeAttribute
    {
        public RequireCompanyAdminAttribute()
        {
            Policy = "CompanyAdmin";
        }
    }

    /// <summary>
    /// Authorization attribute to require company manager role
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequireCompanyManagerAttribute : AuthorizeAttribute
    {
        public RequireCompanyManagerAttribute()
        {
            Policy = "CompanyManager";
        }
    }
}
