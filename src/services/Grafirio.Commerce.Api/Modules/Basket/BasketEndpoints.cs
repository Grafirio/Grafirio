using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Basket;

public static class BasketEndpoints
{
    public static void Map(WebApplication app, ApiVersionSet vs)
    {
        var group = app
            .MapGroup("api/v{version:apiVersion}/baskets")
            .WithTags("Baskets")
            .WithApiVersionSet(vs)
            .RequireAuthorization();

        group.MapGet("/",
            async (BasketService svc, CancellationToken ct) =>
                (await svc.GetBasketAsync(ct)).ToGenericResult())
            .WithName("GetBasket")
            .MapToApiVersion(1, 0);

        group.MapPost("/item",
            async (AddBasketItemRequest req,
                   IValidator<AddBasketItemRequest> validator,
                   BasketService svc,
                   CancellationToken ct) =>
            {
                var v = await validator.ValidateAsync(req, ct);
                if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
                return (await svc.AddItemAsync(req, ct)).ToGenericResult();
            })
            .WithName("AddBasketItem")
            .MapToApiVersion(1, 0);

        group.MapDelete("/item/{itemId:guid}",
            async (Guid itemId, BasketService svc, CancellationToken ct) =>
                (await svc.DeleteItemAsync(itemId, ct)).ToGenericResult())
            .WithName("DeleteBasketItem")
            .MapToApiVersion(1, 0);

        group.MapPut("/apply-discount",
            async (ApplyDiscountRequest req,
                   IValidator<ApplyDiscountRequest> validator,
                   BasketService svc,
                   CancellationToken ct) =>
            {
                var v = await validator.ValidateAsync(req, ct);
                if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
                return (await svc.ApplyDiscountAsync(req, ct)).ToGenericResult();
            })
            .WithName("ApplyDiscount")
            .MapToApiVersion(1, 0);

        group.MapDelete("/remove-discount",
            async (BasketService svc, CancellationToken ct) =>
                (await svc.RemoveDiscountAsync(ct)).ToGenericResult())
            .WithName("RemoveDiscount")
            .MapToApiVersion(1, 0);
    }
}
