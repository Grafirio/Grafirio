using Grafirio.Shared;

namespace Grafirio.File.Api.Features.File.Upload
{
    public record UploadFileCommand(IFormFile File) : IRequestByServiceResult<UploadFileCommandResponse>;
}