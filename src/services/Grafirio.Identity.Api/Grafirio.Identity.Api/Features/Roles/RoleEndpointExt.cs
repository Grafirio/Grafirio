using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Roles.Create;
using Grafirio.Identity.Api.Features.Roles.Delete;
using Grafirio.Identity.Api.Features.Roles.Dtos;
using Grafirio.Identity.Api.Features.Roles.GetByCompany;
using Grafirio.Identity.Api.Features.Roles.Members;
using Grafirio.Identity.Api.Features.Roles.Update;

namespace Grafirio.Identity.Api.Features.Roles;

public static class RoleEndpointExt
{
    public static void AddRoleGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("api/v{version:apiVersion}/roles")
            .WithTags("Roles")
            .WithApiVersionSet(apiVersionSet);

        group.MapGet("/company/{companyId:guid}", async (Guid companyId, IMediator mediator) =>
                (await mediator.Send(new GetCompanyRolesQuery(companyId))).ToGenericResult())
            .WithName("GetCompanyRoles")
            .Produces<List<RoleDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapPost("/", async (CreateRoleCommand command, IMediator mediator) =>
                (await mediator.Send(command)).ToGenericResult())
            .WithName("CreateRole")
            .Produces<CreateRoleResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapPut("/{id:guid}", async (Guid id, UpdateRoleCommand command, IMediator mediator) =>
                (await mediator.Send(command with { Id = id })).ToGenericResult())
            .WithName("UpdateRole")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
                (await mediator.Send(new DeleteRoleCommand(id))).ToGenericResult())
            .WithName("DeleteRole")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        group.MapGet("/{id:guid}/members", async (Guid id, IMediator mediator) =>
                (await mediator.Send(new GetRoleMembersQuery(id))).ToGenericResult())
            .WithName("GetRoleMembers")
            .Produces<List<RoleMemberDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapPost("/{id:guid}/members", async (Guid id, AssignMemberRequest body, IMediator mediator) =>
                (await mediator.Send(new AssignUserToRoleCommand(id, body.KeycloakUserId)))
                    .ToGenericResult())
            .WithName("AssignUserToRole")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapDelete("/{id:guid}/members/{keycloakUserId}",
                async (Guid id, string keycloakUserId, IMediator mediator) =>
                    (await mediator.Send(new RemoveUserFromRoleCommand(id, keycloakUserId)))
                        .ToGenericResult())
            .WithName("RemoveUserFromRole")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        group.MapToApiVersion(1, 0);
    }
}

/// Kullanici kimligi gövdede: yolun icine konsaydi, kullanici kimligi olarak
/// gelen serbest metin route eslesmesini bozabilirdi.
public record AssignMemberRequest(string KeycloakUserId);
