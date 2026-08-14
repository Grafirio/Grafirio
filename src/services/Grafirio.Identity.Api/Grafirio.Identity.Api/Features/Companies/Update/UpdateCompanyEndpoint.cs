namespace Grafirio.Identity.Api.Features.Companies.Update;

public static class UpdateCompanyEndpoint
{
    public static RouteGroupBuilder UpdateCompanyGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateCompanyCommand command, IMediator mediator) =>
            {
                var result = await mediator.Send(command with { Id = id });

                return result.IsSuccess
                    ? Results.Ok(result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("UpdateCompany")
            .Produces<UpdateCompanyResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        return group;
    }
}
