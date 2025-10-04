namespace Grafirio.Identity.Api.Features.Users.Register;

public static class RegisterUserEndpoint
{
    public static RouteGroupBuilder RegisterUserGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/register", async (RegisterUserCommand command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);

                return result.IsSuccess 
                    ? Results.Created(result.UrlAsCreated, result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("RegisterUser")
            .Produces<RegisterUserResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        return group;
    }
}