namespace Grafirio.Commerce.Api.Modules.Catalog;

public record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    string? ImageUrl,
    Guid CategoryId);

public class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}
