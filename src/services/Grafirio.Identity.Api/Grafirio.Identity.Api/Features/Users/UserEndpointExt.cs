using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Users.SetLevel;
using Grafirio.Identity.Api.Features.Users.Dtos;
using Grafirio.Identity.Api.Features.Users.GetByCompany;
using Grafirio.Identity.Api.Features.Users.Register;
using Grafirio.Identity.Api.Features.Users.Revoke;

namespace Grafirio.Identity.Api.Features.Users;

public static class UserEndpointExt
{
    public static void AddUserGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("api/v{version:apiVersion}/users")
            .WithTags("Users")
            .WithApiVersionSet(apiVersionSet);

        group.RegisterUserGroupItemEndpoint();

        group.MapGet("/company/{companyId:guid}",
                async (Guid companyId, bool? includeRevoked, IMediator mediator) =>
                {
                    var result = await mediator.Send(
                        new GetCompanyUsersQuery(companyId, includeRevoked ?? false));

                    return result.IsSuccess
                        ? Results.Ok(result.Data)
                        : Results.BadRequest(result.Fail);
                })
            .WithName("GetCompanyUsers")
            .Produces<List<CompanyMemberDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        // Uc adi "roles" degil "membership": rol artik sirketin kendi
        // tanimladigi izin kumesi ve bambaska bir uctan yonetiliyor.
        group.MapPost("/membership", async (SetMembershipLevelCommand command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);

                return result.IsSuccess
                    ? Results.Ok(result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("SetMembershipLevel")
            .Produces<SetMembershipLevelResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapDelete("/{keycloakUserId}/companies/{companyId:guid}/membership",
                async (string keycloakUserId, Guid companyId, IMediator mediator) =>
                {
                    var result = await mediator.Send(new RevokeMembershipCommand(keycloakUserId, companyId));

                    return result.IsSuccess
                        ? Results.NoContent()
                        : Results.BadRequest(result.Fail);
                })
            .WithName("RevokeMembership")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        group.MapToApiVersion(1, 0);
    }
}
