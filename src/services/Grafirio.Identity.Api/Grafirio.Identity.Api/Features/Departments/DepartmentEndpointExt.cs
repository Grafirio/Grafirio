using Asp.Versioning.Builder;
using Grafirio.Identity.Api.Features.Departments.Create;
using Grafirio.Identity.Api.Features.Departments.Delete;
using Grafirio.Identity.Api.Features.Departments.Dtos;
using Grafirio.Identity.Api.Features.Departments.GetByCompany;
using Grafirio.Identity.Api.Features.Departments.Members;
using Grafirio.Identity.Api.Features.Departments.Update;

namespace Grafirio.Identity.Api.Features.Departments;

public static class DepartmentEndpointExt
{
    public static void AddDepartmentGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
    {
        var group = app.MapGroup("api/v{version:apiVersion}/departments")
            .WithTags("Departments")
            .WithApiVersionSet(apiVersionSet);

        group.MapGet("/company/{companyId:guid}", async (Guid companyId, IMediator mediator) =>
                (await mediator.Send(new GetCompanyDepartmentsQuery(companyId))).ToGenericResult())
            .WithName("GetCompanyDepartments")
            .Produces<List<DepartmentDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapPost("/", async (CreateDepartmentCommand command, IMediator mediator) =>
                (await mediator.Send(command)).ToGenericResult())
            .WithName("CreateDepartment")
            .Produces<CreateDepartmentResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapPut("/{id:guid}", async (Guid id, UpdateDepartmentCommand command, IMediator mediator) =>
                (await mediator.Send(command with { Id = id })).ToGenericResult())
            .WithName("UpdateDepartment")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        group.MapDelete("/{id:guid}", async (Guid id, IMediator mediator) =>
                (await mediator.Send(new DeleteDepartmentCommand(id))).ToGenericResult())
            .WithName("DeleteDepartment")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        group.MapGet("/{id:guid}/members", async (Guid id, IMediator mediator) =>
                (await mediator.Send(new GetDepartmentMembersQuery(id))).ToGenericResult())
            .WithName("GetDepartmentMembers")
            .Produces<List<DepartmentMemberDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapPost("/{id:guid}/members", async (Guid id, AssignMemberRequest body, IMediator mediator) =>
                (await mediator.Send(new AssignUserToDepartmentCommand(id, body.KeycloakUserId)))
                    .ToGenericResult())
            .WithName("AssignUserToDepartment")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        group.MapDelete("/{id:guid}/members/{keycloakUserId}",
                async (Guid id, string keycloakUserId, IMediator mediator) =>
                    (await mediator.Send(new RemoveUserFromDepartmentCommand(id, keycloakUserId)))
                        .ToGenericResult())
            .WithName("RemoveUserFromDepartment")
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
