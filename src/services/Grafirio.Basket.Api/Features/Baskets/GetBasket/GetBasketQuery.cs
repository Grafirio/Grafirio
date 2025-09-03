using Grafirio.Basket.Api.Dto;
using Grafirio.Shared;

namespace Grafirio.Basket.Api.Features.Baskets.GetBasket
{
    public record GetBasketQuery : IRequestByServiceResult<BasketDto>;
}