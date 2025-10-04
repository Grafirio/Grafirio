namespace Grafirio.Identity.Api.Features.Companies.Create;

public static class CreateCompanyEndpoint
{
    public static RouteGroupBuilder CreateCompanyGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateCompanyCommand command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);

                return result.IsSuccess 
                    ? Results.Created(result.UrlAsCreated, result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("CreateCompany")
            .Produces<CreateCompanyResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        return group;
    }
}