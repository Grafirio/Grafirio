using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.GetAll;

public static class GetAllCompaniesEndpoint
{
    public static RouteGroupBuilder GetAllCompaniesGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (IMediator mediator) =>
            {
                var result = await mediator.Send(new GetAllCompaniesQuery());

                return result.IsSuccess 
                    ? Results.Ok(result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("GetAllCompanies")
            .Produces<List<CompanyDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .RequireAuthorization("Password");

        return group;
    }
}