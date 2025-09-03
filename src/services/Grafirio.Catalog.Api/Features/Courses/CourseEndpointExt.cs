using Asp.Versioning.Builder;
using Grafirio.Catalog.Api.Features.Courses.Create;
using Grafirio.Catalog.Api.Features.Courses.Delete;
using Grafirio.Catalog.Api.Features.Courses.GetAll;
using Grafirio.Catalog.Api.Features.Courses.GetAllByUserId;
using Grafirio.Catalog.Api.Features.Courses.GetById;
using Grafirio.Catalog.Api.Features.Courses.Update;

namespace Grafirio.Catalog.Api.Features.Courses
{
    public static class CourseEndpointExt
    {
        public static void AddCourseGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
        {
            app.MapGroup("api/v{version:apiVersion}/courses").WithTags("Courses").WithApiVersionSet(apiVersionSet)
                .CreateCourseGroupItemEndpoint()
                .GetAllCourseGroupItemEndpoint()
                .GetByIdCourseGroupItemEndpoint()
                .UpdateCourseGroupItemEndpoint()
                .DeleteCourseGroupItemEndpoint()
                .GetByUserIdCourseGroupItemEndpoint();
        }
    }
}