namespace Grafirio.Identity.Api.Features.Companies.Onboard;

public static class OnboardCompanyEndpoint
{
    public static RouteGroupBuilder OnboardCompanyGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/onboard", async (OnboardCompanyCommand command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);

                return result.IsSuccess
                    ? Results.Created(result.UrlAsCreated, result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("OnboardCompany")
            .Produces<OnboardCompanyResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .RequireAuthorization("Password");

        return group;
    }
}
