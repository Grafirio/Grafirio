namespace Grafirio.Identity.Api.Features.Companies.Documents.Download;

public static class DownloadCompanyDocumentEndpoint
{
    public static RouteGroupBuilder DownloadCompanyDocumentGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/{companyId:guid}/documents/{documentId:guid}/download",
                async (Guid companyId, Guid documentId, IMediator mediator) =>
                {
                    var result = await mediator.Send(new DownloadCompanyDocumentCommand(companyId, documentId));

                    return result.IsSuccess
                        ? Results.File(result.Data!.Content, result.Data.ContentType, result.Data.FileName)
                        : Results.BadRequest(result.Fail);
                })
            .WithName("DownloadCompanyDocument")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password");

        return group;
    }
}
