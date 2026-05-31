using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Payments;

public static class PaymentEndpoints
{
    public static void Map(WebApplication app, ApiVersionSet vs)
    {
        var group = app
            .MapGroup("api/v{version:apiVersion}/payments")
            .WithTags("Payments")
            .WithApiVersionSet(vs)
            .RequireAuthorization();

        group.MapGet("/",
            async (PaymentService svc, CancellationToken ct) =>
                (await svc.GetMyPaymentsAsync(ct)).ToGenericResult())
            .WithName("GetMyPayments").MapToApiVersion(1, 0);

        group.MapPost("/",
            async (CreatePaymentRequest req,
                   IValidator<CreatePaymentRequest> validator,
                   PaymentService svc,
                   CancellationToken ct) =>
            {
                var v = await validator.ValidateAsync(req, ct);
                if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
                return (await svc.CreateAsync(req, ct)).ToGenericResult();
            })
            .WithName("CreatePayment").MapToApiVersion(1, 0);
    }
}
