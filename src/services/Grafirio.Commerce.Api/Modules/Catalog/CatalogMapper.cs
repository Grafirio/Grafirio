namespace Grafirio.Commerce.Api.Modules.Catalog;

internal static class CatalogMapper
{
    public static ProductDto ToDto(Product p) => new(
        p.Id, p.Name, p.Description, p.Price, p.ImageUrl, p.CategoryId,
        new ProductFeatureDto(p.Feature.Duration, p.Feature.Rating, p.Feature.EducatorFullName));

    public static CategoryDto ToDto(Category c) => new(c.Id, c.Name);
}
