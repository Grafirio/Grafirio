namespace Grafirio.Identity.Api.Features.Companies.Documents.Delete;

public static class DeleteCompanyDocumentEndpoint
{
    public static RouteGroupBuilder DeleteCompanyDocumentGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapDelete("/{companyId:guid}/documents/{documentId:guid}",
                async (Guid companyId, Guid documentId, IMediator mediator) =>
                {
                    var result = await mediator.Send(new DeleteCompanyDocumentCommand(companyId, documentId));

                    return result.IsSuccess
                        ? Results.NoContent()
                        : Results.BadRequest(result.Fail);
                })
            .WithName("DeleteCompanyDocument")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        return group;
    }
}
