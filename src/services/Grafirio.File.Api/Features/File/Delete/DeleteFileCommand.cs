using Grafirio.Shared;

namespace Grafirio.File.Api.Features.File.Delete
{
    public record DeleteFileCommand(string FileName) : IRequestByServiceResult;
}