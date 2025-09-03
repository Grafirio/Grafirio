using AutoMapper;
using Grafirio.Catalog.Api.Features.Categories.Dtos;

namespace Grafirio.Catalog.Api.Features.Categories
{
    public class CategoryMapping : Profile
    {
        public CategoryMapping()
        {
            CreateMap<Category, CategoryDto>().ReverseMap();
        }
    }
}