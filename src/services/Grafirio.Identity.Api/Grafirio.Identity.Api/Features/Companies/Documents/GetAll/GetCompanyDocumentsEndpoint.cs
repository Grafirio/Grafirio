using Grafirio.Identity.Api.Features.Companies.Documents.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.Documents.GetAll;

public static class GetCompanyDocumentsEndpoint
{
    public static RouteGroupBuilder GetCompanyDocumentsGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/{companyId:guid}/documents", async (Guid companyId, IMediator mediator) =>
            {
                var result = await mediator.Send(new GetCompanyDocumentsQuery(companyId));

                return result.IsSuccess
                    ? Results.Ok(result.Data)
                    : Results.BadRequest(result.Fail);
            })
            .WithName("GetCompanyDocuments")
            .Produces<List<CompanyDocumentDto>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .RequireAuthorization("Password");

        return group;
    }
}
