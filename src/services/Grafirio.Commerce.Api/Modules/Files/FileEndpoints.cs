using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Files;

public static class FileEndpoints
{
    public static void Map(WebApplication app, ApiVersionSet vs)
    {
        var group = app
            .MapGroup("api/v{version:apiVersion}/files")
            .WithTags("Files")
            .WithApiVersionSet(vs)
            .RequireAuthorization();

        group.MapPost("/",
            async (IFormFile file, FileService svc, CancellationToken ct) =>
                (await svc.UploadAsync(file, ct)).ToGenericResult())
            .WithName("UploadFile")
            .MapToApiVersion(1, 0)
            .DisableAntiforgery();

        group.MapDelete("/",
            (DeleteFileRequest req, FileService svc) =>
                svc.Delete(req.FileName).ToGenericResult())
            .WithName("DeleteFile")
            .MapToApiVersion(1, 0);
    }
}
