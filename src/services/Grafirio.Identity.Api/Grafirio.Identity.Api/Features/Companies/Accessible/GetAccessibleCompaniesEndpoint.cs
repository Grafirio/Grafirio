namespace Grafirio.Identity.Api.Features.Companies.Accessible;

public static class GetAccessibleCompaniesEndpoint
{
    public static RouteGroupBuilder GetAccessibleCompaniesGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/accessible", async (IMediator mediator) =>
                (await mediator.Send(new GetAccessibleCompaniesQuery())).ToGenericResult())
            .WithName("GetAccessibleCompanies")
            .Produces<List<AccessibleCompanyDto>>(StatusCodes.Status200OK)
            .RequireAuthorization("Password");

        return group;
    }
}
