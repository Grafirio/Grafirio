using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Orders;

public static class OrderEndpoints
{
    public static void Map(WebApplication app, ApiVersionSet vs)
    {
        var group = app
            .MapGroup("api/v{version:apiVersion}/orders")
            .WithTags("Orders")
            .WithApiVersionSet(vs)
            .RequireAuthorization();

        group.MapGet("/",
            async (OrderService svc, CancellationToken ct) =>
                (await svc.GetMyOrdersAsync(ct)).ToGenericResult())
            .WithName("GetMyOrders").MapToApiVersion(1, 0);

        group.MapPost("/",
            async (CreateOrderRequest req,
                   IValidator<CreateOrderRequest> validator,
                   OrderService svc,
                   CancellationToken ct) =>
            {
                var v = await validator.ValidateAsync(req, ct);
                if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
                return (await svc.CreateAsync(req, ct)).ToGenericResult();
            })
            .WithName("CreateOrder").MapToApiVersion(1, 0);
    }
}
