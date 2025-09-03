using Grafirio.Catalog.Api.Features.Courses;
using Grafirio.Catalog.Api.Repositories;

namespace Grafirio.Catalog.Api.Features.Categories
{
    public class Category : BaseEntity
    {
        public string Name { get; set; } = default!;
        public List<Course>? Courses { get; set; }
    }
}