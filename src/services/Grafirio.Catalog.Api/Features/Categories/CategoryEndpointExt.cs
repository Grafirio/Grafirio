using Asp.Versioning.Builder;
using Grafirio.Catalog.Api.Features.Categories.Create;
using Grafirio.Catalog.Api.Features.Categories.GetAll;
using Grafirio.Catalog.Api.Features.Categories.GetById;

namespace Grafirio.Catalog.Api.Features.Categories
{
    public static class CategoryEndpointExt
    {
        public static void AddCategoryGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
        {
            app.MapGroup("api/v{version:apiVersion}/categories").WithTags("Categories")
                .WithApiVersionSet(apiVersionSet)
                .CreateCategoryGroupItemEndpoint()
                .GetAllCategoryGroupItemEndpoint()
                .GetByIdCategoryGroupItemEndpoint();
        }
    }
}