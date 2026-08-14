using Grafirio.Identity.Api.Features.Companies.Documents.Dtos;

namespace Grafirio.Identity.Api.Features.Companies.Documents.Upload;

public static class UploadCompanyDocumentEndpoint
{
    public static RouteGroupBuilder UploadCompanyDocumentGroupItemEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/{companyId:guid}/documents",
                async (Guid companyId, IFormFile file, [FromForm] string documentType,
                    [FromForm] DateTime? expiryDate, [FromForm] string? note,
                    IMediator mediator, CancellationToken ct) =>
                {
                    var result = await mediator.Send(
                        new UploadCompanyDocumentCommand(companyId, file, documentType, expiryDate, note), ct);

                    return result.IsSuccess
                        ? Results.Created(result.UrlAsCreated, result.Data)
                        : Results.BadRequest(result.Fail);
                })
            .WithName("UploadCompanyDocument")
            .Produces<CompanyDocumentDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .RequireAuthorization("Password")
            .DisableAntiforgery();

        return group;
    }
}
