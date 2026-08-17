using Grafirio.Identity.Api.Features.Companies.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.GetChildren;

public static class GetCompanyChildrenEndpoint
{
    public static RouteGroupBuilder GetCompanyChildrenGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/{companyId:guid}/children",
                async (Guid companyId, IMediator mediator) =>
                    (await mediator.Send(new GetCompanyChildrenQuery(companyId))).ToGenericResult())
            .WithName("GetCompanyChildren")
            .Produces<List<CompanyDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        return group;
    }
}
