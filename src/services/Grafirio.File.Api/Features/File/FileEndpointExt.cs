using Asp.Versioning.Builder;
using Grafirio.Discount.Api.Features.Discounts.CreateDiscount;
using Grafirio.File.Api.Features.File.Delete;


namespace Grafirio.File.Api.Features.File
{

    public static class FileEndpointExt
    {
        public static void AddFileGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
        {
            app.MapGroup("api/v{version:apiVersion}/files").WithTags("files").WithApiVersionSet(apiVersionSet)
                .UploadFileGroupItemEndpoint().DeleteFileGroupItemEndpoint().RequireAuthorization();
        }
    }
}