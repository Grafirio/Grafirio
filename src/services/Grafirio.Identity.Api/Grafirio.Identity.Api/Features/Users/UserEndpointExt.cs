using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Users.AssignRole;
using Grafirio.Identity.Api.Features.Users.Dtos;
using Grafirio.Identity.Api.Features.Users.GetByCompany;
using Grafirio.Identity.Api.Features.Users.Register;
using Grafirio.Identity.Api.Features.Users.RevokeRole;

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

        group.MapPost("/roles", async (AssignRoleCommand command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);

                return result.IsSuccess
                    ? Results.Ok(result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("AssignRole")
            .Produces<AssignRoleResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapDelete("/{keycloakUserId}/companies/{companyId:guid}/role",
                async (string keycloakUserId, Guid companyId, IMediator mediator) =>
                {
                    var result = await mediator.Send(new RevokeRoleCommand(keycloakUserId, companyId));

                    return result.IsSuccess
                        ? Results.NoContent()
                        : Results.BadRequest(result.Fail);
                })
            .WithName("RevokeRole")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        group.MapToApiVersion(1, 0);
    }
}
