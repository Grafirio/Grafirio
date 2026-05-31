namespace Grafirio.Commerce.Api.Modules.Catalog;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal Price { get; set; }
    public Guid UserId { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime Created { get; set; }
    public Guid CategoryId { get; set; }
    public ProductFeature Feature { get; set; } = default!;
}
