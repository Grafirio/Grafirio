using AutoMapper;
using Grafirio.Basket.Api.Data;
using Grafirio.Basket.Api.Dto;

namespace Grafirio.Basket.Api.Features.Baskets
{
    public class BasketMapping : Profile
    {
        public BasketMapping()
        {
            CreateMap<BasketDto, Data.Basket>().ReverseMap();
            CreateMap<BasketItemDto, BasketItem>().ReverseMap();
        }
    }
}