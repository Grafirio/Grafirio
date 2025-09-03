using Asp.Versioning.Builder;
using Grafirio.Payment.Api.Feature.Payments.Create;
using Grafirio.Payment.Api.Feature.Payments.GetAllPaymentsByUserId;

namespace Grafirio.Payment.Api.Feature.Payments
{
    public static class PaymentEndpointExt
    {
        public static void AddPaymentGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
        {
            app.MapGroup("api/v{version:apiVersion}/payments").WithTags("payments").WithApiVersionSet(apiVersionSet)
                .CreatePaymentGroupItemEndpoint().GetAllPaymentsByUserIdGroupItemEndpoint();
        }
    }
}