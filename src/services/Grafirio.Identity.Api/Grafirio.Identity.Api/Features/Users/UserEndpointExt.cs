using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Users.Register;

namespace Grafirio.Identity.Api.Features.Users;

public static class UserEndpointExt
{
    public static void AddUserGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
    {
        app.MapGroup("api/v{version:apiVersion}/users")
            .WithTags("Users")
            .WithApiVersionSet(apiVersionSet)
            .RegisterUserGroupItemEndpoint()
            .MapToApiVersion(1, 0);
    }
}