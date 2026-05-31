using Microsoft.Extensions.FileProviders;

namespace Grafirio.Commerce.Api.Modules.Files;

public class FileService(IFileProvider fileProvider)
{
    public async Task<ServiceResult<UploadFileResponse>> UploadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return ServiceResult<UploadFileResponse>.Error("File is empty", HttpStatusCode.BadRequest);

        var newFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var uploadPath  = Path.Combine(fileProvider.GetFileInfo("files").PhysicalPath!, newFileName);

        await using var stream = new FileStream(uploadPath, FileMode.Create);
        await file.CopyToAsync(stream, ct);

        var response = new UploadFileResponse(newFileName, $"files/{newFileName}", file.FileName);
        return ServiceResult<UploadFileResponse>.SuccessAsCreated(response, response.FilePath);
    }

    public ServiceResult Delete(string fileName)
    {
        var fileInfo = fileProvider.GetFileInfo(Path.Combine("files", fileName));
        if (!fileInfo.Exists)
            return ServiceResult.ErrorAsNotFound();

        System.IO.File.Delete(fileInfo.PhysicalPath!);
        return ServiceResult.SuccessAsNoContent();
    }
}
