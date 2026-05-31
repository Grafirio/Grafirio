using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Discount;

public static class DiscountEndpoints
{
    public static void Map(WebApplication app, ApiVersionSet vs)
    {
        var group = app
            .MapGroup("api/v{version:apiVersion}/discounts")
            .WithTags("Discounts")
            .WithApiVersionSet(vs)
            .RequireAuthorization();

        group.MapPost("/",
            async (CreateDiscountRequest req,
                   IValidator<CreateDiscountRequest> validator,
                   DiscountService svc,
                   CancellationToken ct) =>
            {
                var v = await validator.ValidateAsync(req, ct);
                if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
                return (await svc.CreateAsync(req, ct)).ToGenericResult();
            })
            .WithName("CreateDiscount").MapToApiVersion(1, 0);

        group.MapGet("/{code:length(10)}",
            async (string code, DiscountService svc, CancellationToken ct) =>
                (await svc.GetByCodeAsync(code, ct)).ToGenericResult())
            .WithName("GetDiscountByCode").MapToApiVersion(1, 0);
    }
}
