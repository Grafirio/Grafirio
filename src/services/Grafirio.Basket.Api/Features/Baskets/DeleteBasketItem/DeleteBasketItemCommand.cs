using Grafirio.Shared;

namespace Grafirio.Basket.Api.Features.Baskets.DeleteBasketItem
{
    public record DeleteBasketItemCommand(Guid Id) : IRequestByServiceResult;
}