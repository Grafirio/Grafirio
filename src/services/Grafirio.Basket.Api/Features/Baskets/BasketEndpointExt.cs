using Asp.Versioning.Builder;
using Grafirio.Basket.Api.Features.Baskets.AddBasketItem;
using Grafirio.Basket.Api.Features.Baskets.ApplyDiscountCoupon;
using Grafirio.Basket.Api.Features.Baskets.DeleteBasketItem;
using Grafirio.Basket.Api.Features.Baskets.GetBasket;
using Grafirio.Basket.Api.Features.Baskets.RemoveDiscountCoupon;

namespace Grafirio.Basket.Api.Features.Baskets
{
    public static class BasketEndpointExt
    {
        public static void AddBasketGroupEndpointExt(this WebApplication app, ApiVersionSet apiVersionSet)
        {
            app.MapGroup("api/v{version:apiVersion}/baskets").WithTags("Baskets")
                .WithApiVersionSet(apiVersionSet)
                .AddBasketItemGroupItemEndpoint()
                .DeleteBasketItemGroupItemEndpoint()
                .GetBasketGroupItemEndpoint()
                .ApplyDiscountCouponGroupItemEndpoint()
                .RemoveDiscountCouponGroupItemEndpoint();
        }
    }
}