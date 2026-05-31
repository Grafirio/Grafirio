namespace Grafirio.Commerce.Api.Modules.Catalog;

public record ProductDto(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string? ImageUrl,
    Guid CategoryId,
    ProductFeatureDto Feature);
