namespace Grafirio.Commerce.Api.Modules.Catalog;

public record CreateCategoryRequest(string Name);

public class CreateCategoryValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
